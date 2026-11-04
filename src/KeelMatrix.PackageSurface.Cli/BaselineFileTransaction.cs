using System.Runtime.InteropServices;
using KeelMatrix.PackageSurface.Probe;
using Microsoft.Win32.SafeHandles;

namespace KeelMatrix.PackageSurface;

public sealed class BaselineFileTransaction
{
    private const long MaxBaselineBytes = 16 * 1024 * 1024;
    private readonly string path;
    private readonly string backupPath;
    private readonly bool existed;
    private bool began;

    private BaselineFileTransaction(string path, bool existed)
    {
        this.path = path;
        this.existed = existed;
        backupPath = path + ".rollback-" + Guid.NewGuid().ToString("N");
    }

    public static BaselineFileTransaction Prepare(string path, string inputPath, ProjectSelection selection)
    {
        var fullPath = Path.GetFullPath(path);
        var candidates = new List<string> { Path.GetFullPath(inputPath) };
        foreach (var project in selection.Projects)
        {
            candidates.Add(project.ProjectPath);
            candidates.Add(project.AssetsPath);
            var generatedDirectory = Path.GetDirectoryName(project.AssetsPath)!;
            var projectName = Path.GetFileName(project.ProjectPath);
            candidates.Add(Path.Combine(generatedDirectory, projectName + ".nuget.g.props"));
            candidates.Add(Path.Combine(generatedDirectory, projectName + ".nuget.g.targets"));
            candidates.AddRange(ResolvedGraphClassifier.GetReachablePackageInputPaths(project.AssetsPath));
        }

        foreach (var candidate in candidates.Distinct(FileSystemComparisonScope.PathComparer))
        {
            if (PathsReferToSameFile(fullPath, candidate))
            {
                throw new InvalidDataException("The baseline output must not alias the selected solution, project, restore, generated-import, or reachable package input.");
            }
        }

        var existed = File.Exists(fullPath);
        if (Directory.Exists(fullPath)) throw new InvalidDataException("The baseline output path is a directory.");
        if (existed)
        {
            var info = new FileInfo(fullPath);
            if (info.Length > MaxBaselineBytes) throw new InvalidDataException("The existing baseline output exceeds the supported size limit.");
            if (info.IsReadOnly || (OperatingSystem.IsWindows() ? false : (File.GetUnixFileMode(fullPath) & (UnixFileMode.UserWrite | UnixFileMode.GroupWrite | UnixFileMode.OtherWrite)) == 0))
            {
                throw new UnauthorizedAccessException("The existing baseline output is read-only.");
            }
        }

        return new BaselineFileTransaction(fullPath, existed);
    }

    public void Begin()
    {
        if (began) return;
        if (existed) File.Move(path, backupPath);
        began = true;
    }

    public void Commit()
    {
        if (began && existed && File.Exists(backupPath)) File.Delete(backupPath);
        began = false;
    }

    public void Rollback()
    {
        if (!began) return;
        var restored = false;
        try
        {
            if (File.Exists(path)) File.Delete(path);
            if (!existed)
            {
                restored = true;
            }
            else
            {
                File.Move(backupPath, path);
                restored = true;
            }
        }
        finally
        {
            if (restored)
            {
                if (File.Exists(backupPath)) File.Delete(backupPath);
                began = false;
            }
        }
    }

    private static bool PathsReferToSameFile(string left, string right)
    {
        if (FileSystemComparisonScope.PathsEqual(left, right)) return true;
        if (HasReparsePoint(left) || HasReparsePoint(right)) throw new InvalidDataException("The baseline output or an analysis input uses a symlink or reparse-point path.");
        if (!File.Exists(left) || !File.Exists(right)) return false;

        if (!FileIdentity.TryRead(left, out var leftIdentity) || !FileIdentity.TryRead(right, out var rightIdentity))
        {
            throw new InvalidDataException("The baseline alias could not be validated using file identity.");
        }

        return leftIdentity == rightIdentity;
    }

    private readonly record struct FileIdentity(ulong DeviceOrVolume, ulong FileNumber)
    {
        public static bool TryRead(string path, out FileIdentity identity)
        {
            identity = default;
            try
            {
                using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                return OperatingSystem.IsWindows()
                    ? TryReadWindows(handle, out identity)
                    : TryReadUnix(handle, out identity);
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
            catch (PlatformNotSupportedException)
            {
                return false;
            }
        }

        private static bool TryReadWindows(SafeFileHandle handle, out FileIdentity identity)
        {
            identity = default;
            if (!GetFileInformationByHandle(handle, out var information)) return false;
            identity = new FileIdentity(information.VolumeSerialNumber, ((ulong)information.FileIndexHigh << 32) | information.FileIndexLow);
            return true;
        }

        private static bool TryReadUnix(SafeFileHandle handle, out FileIdentity identity)
        {
            identity = default;
            var buffer = Marshal.AllocHGlobal(256);
            try
            {
                if (FStat(handle.DangerousGetHandle(), buffer) != 0) return false;
                identity = new FileIdentity(
                    unchecked((ulong)Marshal.ReadInt64(buffer, 0)),
                    unchecked((ulong)Marshal.ReadInt64(buffer, 8)));
                return true;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetFileInformationByHandle(SafeFileHandle file, out ByHandleFileInformation information);

        [DllImport("libc", EntryPoint = "fstat", SetLastError = true)]
        private static extern int FStat(IntPtr fileDescriptor, IntPtr buffer);

        [StructLayout(LayoutKind.Sequential)]
        private struct ByHandleFileInformation
        {
            public uint FileAttributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
            public uint VolumeSerialNumber;
            public uint FileSizeHigh;
            public uint FileSizeLow;
            public uint NumberOfLinks;
            public uint FileIndexHigh;
            public uint FileIndexLow;
        }
    }

    private static bool HasReparsePoint(string path)
    {
        var full = Path.GetFullPath(path);
        try
        {
            if ((File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0) return true;
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        var currentPath = full;
        while (!File.Exists(currentPath) && !Directory.Exists(currentPath))
        {
            var parentPath = Path.GetDirectoryName(currentPath);
            if (string.IsNullOrWhiteSpace(parentPath) || parentPath == currentPath) break;
            currentPath = parentPath;
        }

        var current = new DirectoryInfo(File.Exists(full) ? Path.GetDirectoryName(full)! : currentPath);
        var pathRoot = Path.GetPathRoot(full);
        if (File.Exists(full))
        {
            var file = new FileInfo(full);
            if ((file.Attributes & FileAttributes.ReparsePoint) != 0 || file.LinkTarget is not null) return true;
        }

        while (current is not null)
        {
            // macOS exposes /var through the standard /private/var system alias.
            // Caller-controlled root aliases and links below /var remain rejected.
            if (!IsSystemRootAlias(current, pathRoot) &&
                ((current.Attributes & FileAttributes.ReparsePoint) != 0 || current.LinkTarget is not null)) return true;
            current = current.Parent;
        }

        return false;
    }

    private static bool IsSystemRootAlias(DirectoryInfo directory, string? pathRoot)
        => IsSystemRootAlias(directory, pathRoot, OperatingSystem.IsMacOS());

    private static bool IsSystemRootAlias(DirectoryInfo directory, string? pathRoot, bool isMacOs)
    {
        if (!isMacOs || pathRoot is null || directory.Parent is null ||
            !string.Equals(directory.Parent.FullName, pathRoot, StringComparison.Ordinal) ||
            !string.Equals(directory.Name, "var", StringComparison.Ordinal)) return false;

        var target = directory.LinkTarget;
        if (string.IsNullOrWhiteSpace(target)) return false;
        var targetPath = Path.IsPathRooted(target) ? target : Path.Combine(directory.Parent.FullName, target);
        return string.Equals(Path.GetFullPath(targetPath), Path.Combine(pathRoot, "private", directory.Name), StringComparison.Ordinal);
    }
}
