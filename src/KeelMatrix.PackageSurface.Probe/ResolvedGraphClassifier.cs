using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace KeelMatrix.PackageSurface.Probe;

public static class ResolvedGraphClassifier
{
    private static readonly Regex PropertyComparison = new(
        "^\\s*['\\\"]?\\$\\(\\s*(?<property>[A-Za-z_][A-Za-z0-9_.-]*)\\s*\\)['\\\"]?\\s*(?<operator>==|!=)\\s*['\\\"](?<value>[^'\\\"]*)['\\\"]\\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ExistsCondition = new(
        "^\\s*Exists\\s*\\(\\s*['\\\"](?<path>[^'\\\"]*)['\\\"]\\s*\\)\\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static ProbeResult Analyze(string assetsFile, string projectRoot)
    {
        var incomplete = new List<string>();
        try
        {
            using var stream = File.OpenRead(assetsFile);
            using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 128 });
            var root = document.RootElement;
            var packageFolders = ReadPackageFolders(root, incomplete);
            var libraries = root.TryGetProperty("libraries", out var librariesElement)
                ? librariesElement
                : throw new InvalidDataException("project.assets.json has no libraries object.");
            var directPackages = ReadDirectPackages(root);
            var targetAliases = ReadTargetAliases(root);
            var generatedImports = ReadGeneratedImports(projectRoot, incomplete);
            var entries = new List<SurfaceEntry>();
            var projectEntries = new Dictionary<string, SurfaceEntry>(StringComparer.OrdinalIgnoreCase);

            if (!root.TryGetProperty("targets", out var targets))
            {
                throw new InvalidDataException("project.assets.json has no targets object.");
            }

            foreach (var target in targets.EnumerateObject())
            {
                var (tfm, rid) = SplitTarget(target.Name);
                var targetAlias = targetAliases.TryGetValue(target.Name, out var alias) ? alias : tfm;
                foreach (var package in target.Value.EnumerateObject())
                {
                    var (packageId, version) = SplitPackageKey(package.Name);
                    var libraryKey = packageId + "/" + version;
                    if (!libraries.TryGetProperty(libraryKey, out var library))
                    {
                        incomplete.Add($"Target {target.Name} refers to missing library {libraryKey}.");
                        continue;
                    }

                    if (!library.TryGetProperty("path", out var pathElement))
                    {
                        incomplete.Add($"Library {libraryKey} has no package path.");
                        continue;
                    }

                    var packageRoot = ResolvePackageRoot(packageId, version, pathElement.GetString()!, packageFolders, incomplete);
                    var files = ReadLibraryFiles(library, libraryKey, incomplete);
                    var targetAssets = package.Value;
                    foreach (var relativePath in files)
                    {
                        if (!TryGetCapability(relativePath, out var capability))
                        {
                            continue;
                        }

                        var physicalPath = Path.Combine(packageRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
                        var present = File.Exists(physicalPath);
                        var reason = present ? null : $"Reachable asset is missing from resolved package contents: {relativePath}.";
                        if (!present)
                        {
                            incomplete.Add($"{libraryKey}: {reason}");
                        }

                        var context = capability == CapabilityKind.BuildMultiTargeting
                            ? SurfaceContextKind.Project
                            : SurfaceContextKind.Target;
                        var active = IsActive(capability, relativePath, packageRoot, targetAssets, context, targetAlias, generatedImports);
                        var sha = present ? ComputeSha256(physicalPath) : null;
                        var entry = new SurfaceEntry(
                            context == SurfaceContextKind.Project ? null : tfm,
                            context == SurfaceContextKind.Project ? null : rid,
                            context,
                            packageId,
                            version,
                            directPackages.Contains(packageId) ? "direct" : "transitive",
                            capability,
                            relativePath,
                            present,
                            active,
                            sha,
                            !present,
                            reason);
                        if (context == SurfaceContextKind.Project)
                        {
                            var key = string.Join("|", entry.PackageId, entry.Version, entry.Capability, entry.PackageRelativePath);
                            projectEntries[key] = entry;
                        }
                        else
                        {
                            entries.Add(entry);
                        }
                    }
                }
            }

            entries.AddRange(projectEntries.Values);
            return new ProbeResult(Deduplicate(entries), incomplete.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            incomplete.Add(ex.Message);
            return new ProbeResult(Array.Empty<SurfaceEntry>(), incomplete.Distinct(StringComparer.Ordinal).ToArray());
        }
    }

    private static SurfaceEntry[] Deduplicate(IEnumerable<SurfaceEntry> entries) => entries
        .GroupBy(entry => string.Join("|", entry.Context, entry.TargetFramework, entry.RuntimeIdentifier, entry.PackageId, entry.Version, entry.Capability, entry.PackageRelativePath), StringComparer.OrdinalIgnoreCase)
        .Select(group => group.OrderByDescending(entry => entry.Active).First())
        .OrderBy(entry => entry.Context)
        .ThenBy(entry => entry.TargetFramework, StringComparer.Ordinal)
        .ThenBy(entry => entry.RuntimeIdentifier, StringComparer.Ordinal)
        .ThenBy(entry => entry.PackageId, StringComparer.OrdinalIgnoreCase)
        .ThenBy(entry => entry.Capability)
        .ThenBy(entry => entry.PackageRelativePath, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static HashSet<string> ReadDirectPackages(JsonElement root)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!root.TryGetProperty("project", out var project) || !project.TryGetProperty("frameworks", out var frameworks))
        {
            return result;
        }

        foreach (var framework in frameworks.EnumerateObject())
        {
            if (!framework.Value.TryGetProperty("dependencies", out var dependencies))
            {
                continue;
            }

            foreach (var dependency in dependencies.EnumerateObject())
            {
                result.Add(dependency.Name);
            }
        }

        return result;
    }

    private static Dictionary<string, string> ReadTargetAliases(JsonElement root)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!root.TryGetProperty("project", out var project) || !project.TryGetProperty("frameworks", out var frameworks))
        {
            return result;
        }

        foreach (var framework in frameworks.EnumerateObject())
        {
            if (framework.Value.TryGetProperty("targetAlias", out var alias) && alias.ValueKind == JsonValueKind.String)
            {
                var targetKey = framework.Name;
                result[targetKey] = alias.GetString()!;
            }
        }

        return result;
    }

    private static string[] ReadPackageFolders(JsonElement root, List<string> incomplete)
    {
        if (!root.TryGetProperty("packageFolders", out var folders))
        {
            incomplete.Add("project.assets.json has no packageFolders object.");
            return Array.Empty<string>();
        }

        return folders.EnumerateObject().Select(folder => folder.Name).ToArray();
    }

    private static IReadOnlyList<string> ReadLibraryFiles(JsonElement library, string libraryKey, List<string> incomplete)
    {
        if (!library.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array)
        {
            incomplete.Add($"Library {libraryKey} has no files array.");
            return Array.Empty<string>();
        }

        var result = new List<string>();
        foreach (var file in files.EnumerateArray())
        {
            if (file.ValueKind != JsonValueKind.String || !TryNormalizeRelativePath(file.GetString()!, out var normalized))
            {
                incomplete.Add($"Library {libraryKey} contains an invalid relative asset path.");
                continue;
            }

            result.Add(normalized);
        }

        return result;
    }

    private static string ResolvePackageRoot(string packageId, string version, string libraryPath, string[] packageFolders, List<string> incomplete)
    {
        if (!TryNormalizeRelativePath(libraryPath, out var normalizedLibraryPath))
        {
            incomplete.Add($"Package {packageId}/{version} has an unsafe cache-relative path.");
            return Path.GetTempPath();
        }

        foreach (var folder in packageFolders)
        {
            var candidate = Path.GetFullPath(Path.Combine(folder, normalizedLibraryPath.Replace('/', Path.DirectorySeparatorChar)));
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        incomplete.Add($"Package {packageId}/{version} was not found in the resolved package folders.");
        var firstFolder = packageFolders.Length > 0 ? packageFolders[0] : Path.GetTempPath();
        return Path.GetFullPath(Path.Combine(firstFolder, normalizedLibraryPath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static List<GeneratedImport> ReadGeneratedImports(string projectRoot, List<string> incomplete)
    {
        var result = new List<GeneratedImport>();
        foreach (var file in Directory.EnumerateFiles(projectRoot, "*.nuget.g.*", SearchOption.AllDirectories))
        {
            if (!file.EndsWith(".props", StringComparison.OrdinalIgnoreCase) && !file.EndsWith(".targets", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, MaxCharactersFromEntities = 0, IgnoreComments = true, IgnoreWhitespace = true };
                using var reader = XmlReader.Create(file, settings);
                var xml = XDocument.Load(reader, LoadOptions.None);
                foreach (var import in xml.Descendants().Where(element => element.Name.LocalName == "Import"))
                {
                    var project = import.Attribute("Project")?.Value;
                    if (project is not null)
                    {
                        var conditions = import.AncestorsAndSelf()
                            .Select(element => new ConditionClause(
                                element.Name.LocalName,
                                element.Attribute("Condition")?.Value))
                            .Where(clause => !string.IsNullOrWhiteSpace(clause.Expression))
                            .ToArray();
                        var applicability = DetermineApplicability(conditions, project, out var reason);
                        if (!applicability.IsKnown)
                        {
                            incomplete.Add($"Generated import {Path.GetFileName(file)} has an unproven condition on {reason!.Owner}: {reason.Message}");
                        }

                        result.Add(new GeneratedImport(Path.GetFileName(file), project, applicability));
                    }
                }
            }
            catch (XmlException ex)
            {
                incomplete.Add($"Generated import file {Path.GetFileName(file)} is malformed: {ex.Message}");
            }
        }

        return result;
    }

    private static bool IsActive(
        CapabilityKind capability,
        string relativePath,
        string packageRoot,
        JsonElement targetAssets,
        SurfaceContextKind context,
        string targetFramework,
        IReadOnlyList<GeneratedImport> generatedImports)
    {
        if (capability == CapabilityKind.ToolOrScriptPresent)
        {
            return false;
        }

        if (capability is CapabilityKind.BuildProps or CapabilityKind.BuildTargets or CapabilityKind.BuildTransitive or CapabilityKind.BuildMultiTargeting)
        {
            var fullAsset = NormalizeText(Path.Combine(packageRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            return generatedImports.Any(import =>
            {
                var normalizedImport = NormalizeText(import.Project);
                return (normalizedImport.Contains(fullAsset, StringComparison.OrdinalIgnoreCase) ||
                        normalizedImport.EndsWith('/' + relativePath, StringComparison.OrdinalIgnoreCase) ||
                        normalizedImport.EndsWith('\\' + relativePath.Replace('/', '\\'), StringComparison.OrdinalIgnoreCase)) &&
                       import.AppliesTo(context, targetFramework, capability == CapabilityKind.BuildMultiTargeting);
            });
        }

        var assetName = relativePath.Replace('\\', '/');
        return capability switch
        {
            CapabilityKind.CompilerExtension => IsConventionalAnalyzerPath(assetName) && IsReachablePackage(targetAssets),
            CapabilityKind.CompileSourceInjection => ContainsAsset(targetAssets, "contentFiles", assetName),
            CapabilityKind.NativeRuntime => ContainsAsset(targetAssets, "native", assetName) || ContainsAsset(targetAssets, "runtime", assetName),
            _ => false
        };
    }

    private static bool ContainsAsset(JsonElement targetAssets, string propertyName, string assetName)
    {
        if (!targetAssets.TryGetProperty(propertyName, out var value))
        {
            return false;
        }

        return EnumerateStrings(value).Any(path =>
            string.Equals(path.Replace('\\', '/'), assetName, StringComparison.OrdinalIgnoreCase) ||
            path.Replace('\\', '/').EndsWith('/' + assetName, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsConventionalAnalyzerPath(string path) =>
        path.StartsWith("analyzers/dotnet/cs/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("analyzers/dotnet/vb/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("analyzers/dotnet/", StringComparison.OrdinalIgnoreCase) && path.Count(character => character == '/') == 2;

    private static bool IsReachablePackage(JsonElement targetAssets) => targetAssets.ValueKind == JsonValueKind.Object;

    private static IEnumerable<string> EnumerateStrings(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            yield return element.GetString()!;
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                foreach (var value in EnumerateStrings(item))
                {
                    yield return value;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                yield return property.Name;
                foreach (var value in EnumerateStrings(property.Value))
                {
                    yield return value;
                }
            }
        }
    }

    private static bool TryGetCapability(string path, out CapabilityKind capability)
    {
        capability = default;
        var normalized = path.Replace('\\', '/');
        var extension = Path.GetExtension(normalized);
        if (normalized.StartsWith("buildTransitive/", StringComparison.OrdinalIgnoreCase))
        {
            capability = CapabilityKind.BuildTransitive;
            return extension is ".props" or ".targets";
        }

        if (normalized.StartsWith("buildMultiTargeting/", StringComparison.OrdinalIgnoreCase))
        {
            capability = CapabilityKind.BuildMultiTargeting;
            return extension is ".props" or ".targets";
        }

        if (normalized.StartsWith("build/", StringComparison.OrdinalIgnoreCase))
        {
            capability = extension.Equals(".props", StringComparison.OrdinalIgnoreCase) ? CapabilityKind.BuildProps : CapabilityKind.BuildTargets;
            return extension is ".props" or ".targets";
        }

        if (normalized.StartsWith("analyzers/", StringComparison.OrdinalIgnoreCase) && extension is ".dll" or ".exe")
        {
            capability = CapabilityKind.CompilerExtension;
            return true;
        }

        if (normalized.StartsWith("contentFiles/", StringComparison.OrdinalIgnoreCase) && extension.Equals(".cs", StringComparison.OrdinalIgnoreCase))
        {
            capability = CapabilityKind.CompileSourceInjection;
            return true;
        }

        if (normalized.StartsWith("runtimes/", StringComparison.OrdinalIgnoreCase) && normalized.Contains("/native/", StringComparison.OrdinalIgnoreCase))
        {
            capability = CapabilityKind.NativeRuntime;
            return true;
        }

        if (normalized.StartsWith("tools/", StringComparison.OrdinalIgnoreCase) || normalized.StartsWith("scripts/", StringComparison.OrdinalIgnoreCase))
        {
            capability = CapabilityKind.ToolOrScriptPresent;
            return true;
        }

        return false;
    }

    private static (string Tfm, string? Rid) SplitTarget(string target)
    {
        var slash = target.IndexOf('/');
        return slash < 0 ? (target, null) : (target[..slash], target[(slash + 1)..]);
    }

    private static (string Id, string Version) SplitPackageKey(string key)
    {
        var slash = key.LastIndexOf('/');
        return slash < 0 ? (key, string.Empty) : (key[..slash], key[(slash + 1)..]);
    }

    private static bool TryNormalizeRelativePath(string value, out string normalized)
    {
        normalized = value.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(normalized) || normalized.StartsWith('/') || Path.IsPathRooted(normalized) || normalized.Split('/').Any(part => part == ".."))
        {
            normalized = string.Empty;
            return false;
        }

        return true;
    }

    private static string NormalizeText(string value) => value.Replace('\\', '/').Trim();

    private static ImportApplicability DetermineApplicability(
        IReadOnlyList<ConditionClause> conditions,
        string importProject,
        out ConditionFailure? failure)
    {
        failure = null;
        ConditionNode? combined = null;
        foreach (var condition in conditions)
        {
            var parsed = ParseCondition(condition.Expression!, importProject);
            if (!parsed.IsKnown)
            {
                failure = new ConditionFailure(condition.Owner, parsed.Reason!);
                return new ImportApplicability(null, parsed.Reason);
            }

            combined = combined is null ? parsed.Node : new AndCondition(combined, parsed.Node!);
        }

        return ImportApplicability.Known(combined);
    }

    private static ParsedCondition ParseCondition(string expression, string importProject)
    {
        var normalized = StripOuterParentheses(expression.Trim());
        if (normalized.Length == 0)
        {
            return ParsedCondition.Unknown("the condition is empty");
        }

        if (TrySplitTopLevel(normalized, "OR", out var disjunction))
        {
            return CombineConditions(disjunction, importProject, static (left, right) => new OrCondition(left, right));
        }

        if (TrySplitTopLevel(normalized, "AND", out var conjunction))
        {
            return CombineConditions(conjunction, importProject, static (left, right) => new AndCondition(left, right));
        }

        var comparison = PropertyComparison.Match(normalized);
        if (comparison.Success)
        {
            var property = comparison.Groups["property"].Value;
            var @operator = comparison.Groups["operator"].Value;
            var value = comparison.Groups["value"].Value;
            if (property.Equals("TargetFramework", StringComparison.OrdinalIgnoreCase))
            {
                return ParsedCondition.Known(new PropertyComparisonCondition(property, @operator, value));
            }

            if (property.Equals("ExcludeRestorePackageImports", StringComparison.OrdinalIgnoreCase) &&
                @operator == "!=" && value.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                return ParsedCondition.Known(new ConstantCondition(true));
            }

            return ParsedCondition.Unknown($"property '{property}' is outside the supported condition grammar");
        }

        var exists = ExistsCondition.Match(normalized);
        if (exists.Success)
        {
            if (TryProveExists(exists.Groups["path"].Value, importProject, out var existsValue, out var reason))
            {
                return ParsedCondition.Known(new ConstantCondition(existsValue));
            }

            return ParsedCondition.Unknown(reason!);
        }

        return ParsedCondition.Unknown("the expression is outside the supported condition grammar");
    }

    private static ParsedCondition CombineConditions(
        IReadOnlyList<string> expressions,
        string importProject,
        Func<ConditionNode, ConditionNode, ConditionNode> combine)
    {
        ConditionNode? combined = null;
        foreach (var expression in expressions)
        {
            var parsed = ParseCondition(expression, importProject);
            if (!parsed.IsKnown)
            {
                return ParsedCondition.Unknown($"subexpression '{expression}' is unproven: {parsed.Reason}");
            }

            combined = combined is null ? parsed.Node : combine(combined, parsed.Node!);
        }

        return ParsedCondition.Known(combined!);
    }

    private static bool TryProveExists(string requestedPath, string importProject, out bool exists, out string? reason)
    {
        exists = false;
        reason = null;
        var requested = NormalizeText(requestedPath);
        var project = NormalizeText(importProject);
        if (requested.StartsWith("$(NuGetPackageRoot)/", StringComparison.OrdinalIgnoreCase))
        {
            var suffix = requested["$(NuGetPackageRoot)/".Length..];
            if (suffix.Contains("$(", StringComparison.Ordinal) ||
                !project.EndsWith('/' + suffix, StringComparison.OrdinalIgnoreCase))
            {
                reason = "Exists(...) is not the standard resolved-package-file guard";
                return false;
            }

            // Generated NuGet imports retain $(NuGetPackageRoot) in both the
            // Import and Exists expressions. The resolved package file is
            // checked independently when the asset entry is created.
            exists = true;
            return true;
        }

        if (Path.IsPathRooted(requestedPath) &&
            string.Equals(requested, project, StringComparison.OrdinalIgnoreCase))
        {
            exists = File.Exists(importProject);
            return true;
        }

        reason = "Exists(...) is supported only for the standard resolved-package-file guard";
        return false;
    }

    private static string StripOuterParentheses(string expression)
    {
        while (expression.Length >= 2 && expression[0] == '(' && expression[^1] == ')' && HasSingleOuterPair(expression))
        {
            expression = expression[1..^1].Trim();
        }

        return expression;
    }

    private static bool HasSingleOuterPair(string expression)
    {
        var depth = 0;
        var quote = '\0';
        for (var index = 0; index < expression.Length; index++)
        {
            var character = expression[index];
            if (quote != '\0')
            {
                if (character == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (character is '\'' or '\"')
            {
                quote = character;
            }
            else if (character == '(')
            {
                depth++;
            }
            else if (character == ')' && --depth == 0 && index != expression.Length - 1)
            {
                return false;
            }
        }

        return depth == 0 && quote == '\0';
    }

    private static bool TrySplitTopLevel(string expression, string operatorText, out string[] parts)
    {
        var matches = new List<string>();
        var start = 0;
        var depth = 0;
        var quote = '\0';
        for (var index = 0; index < expression.Length; index++)
        {
            var character = expression[index];
            if (quote != '\0')
            {
                if (character == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (character is '\'' or '\"')
            {
                quote = character;
                continue;
            }

            if (character == '(')
            {
                depth++;
                continue;
            }

            if (character == ')')
            {
                depth--;
                continue;
            }

            if (depth != 0 || index + operatorText.Length > expression.Length ||
                !expression.AsSpan(index, operatorText.Length).Equals(operatorText.AsSpan(), StringComparison.OrdinalIgnoreCase) ||
                !IsConditionBoundary(expression, index - 1) ||
                !IsConditionBoundary(expression, index + operatorText.Length))
            {
                continue;
            }

            matches.Add(expression[start..index].Trim());
            start = index + operatorText.Length;
            index += operatorText.Length - 1;
        }

        if (matches.Count == 0)
        {
            parts = Array.Empty<string>();
            return false;
        }

        matches.Add(expression[start..].Trim());
        parts = matches.ToArray();
        return true;
    }

    private static bool IsConditionBoundary(string expression, int index) =>
        index < 0 || index >= expression.Length || !char.IsLetterOrDigit(expression[index]) && expression[index] != '_';

    private sealed record ConditionClause(string Owner, string? Expression);

    private sealed record ConditionFailure(string Owner, string Message);

    private sealed record ParsedCondition(ConditionNode? Node, string? Reason)
    {
        public bool IsKnown => Reason is null;

        public static ParsedCondition Known(ConditionNode node) => new(node, null);

        public static ParsedCondition Unknown(string reason) => new(null, reason);
    }

    private abstract record ConditionNode
    {
        public abstract bool Evaluate(SurfaceContextKind context, string targetFramework);
    }

    private sealed record ConstantCondition(bool Value) : ConditionNode
    {
        public override bool Evaluate(SurfaceContextKind context, string targetFramework) => Value;
    }

    private sealed record PropertyComparisonCondition(string Property, string Operator, string Value) : ConditionNode
    {
        public override bool Evaluate(SurfaceContextKind context, string targetFramework)
        {
            var actual = context == SurfaceContextKind.Project ? string.Empty : targetFramework;
            var equal = string.Equals(actual, Value, StringComparison.OrdinalIgnoreCase);
            return Operator == "==" ? equal : !equal;
        }
    }

    private sealed record AndCondition(ConditionNode Left, ConditionNode Right) : ConditionNode
    {
        public override bool Evaluate(SurfaceContextKind context, string targetFramework) =>
            Left.Evaluate(context, targetFramework) && Right.Evaluate(context, targetFramework);
    }

    private sealed record OrCondition(ConditionNode Left, ConditionNode Right) : ConditionNode
    {
        public override bool Evaluate(SurfaceContextKind context, string targetFramework) =>
            Left.Evaluate(context, targetFramework) || Right.Evaluate(context, targetFramework);
    }

    private sealed record ImportApplicability(ConditionNode? Condition, string? Failure)
    {
        public bool IsKnown => Failure is null;

        public static ImportApplicability Known(ConditionNode? condition) => new(condition, null);

        public bool AppliesTo(SurfaceContextKind context, string targetFramework) =>
            IsKnown && (Condition?.Evaluate(context, targetFramework) ?? true);
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string? GetString(this JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private sealed record GeneratedImport(
        string SourceFile,
        string Project,
        ImportApplicability Applicability)
    {
        public bool AppliesTo(SurfaceContextKind context, string targetFramework, bool projectLevelCapability) =>
            context == (projectLevelCapability ? SurfaceContextKind.Project : SurfaceContextKind.Target) &&
            Applicability.AppliesTo(context, targetFramework);
    }
}
