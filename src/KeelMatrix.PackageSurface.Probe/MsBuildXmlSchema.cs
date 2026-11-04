using System.Xml;
using static KeelMatrix.PackageSurface.Probe.AnalysisLimits;


namespace KeelMatrix.PackageSurface.Probe;

internal static class MsBuildXmlSchema
{
    internal static readonly string[] PackageMsBuildElementNames = { "UsingTask", "Exec", "Import", "Code" };

    internal static readonly string[] PackageMsBuildAttributeNames =
    {
        "AfterTargets",
        "AssemblyFile",
        "BeforeTargets",
        "Command",
        "Condition",
        "DependsOnTargets",
        "Include",
        "Language",
        "Name",
        "Project",
        "TaskFactory",
        "TaskName",
        "ToolsVersion",
        "Type"
    };

    // These built-in MSBuild names are exempt from near-spelling checks only so
    // standard unconsumed metadata cannot be mistaken for a consumed element.
    // Other unconsumed item/property/task names remain tolerated by default.
    internal static readonly HashSet<string> PackageMsBuildStandardElementNames = new(StringComparer.Ordinal)
    {
        "Project",
        "PropertyGroup",
        "ItemGroup",
        "ItemDefinitionGroup",
        "ImportGroup",
        "ItemMetadata",
        "Target",
        "Choose",
        "When",
        "Otherwise",
        "OnError",
        "Parameter",
        "ParameterGroup",
        "ProjectExtensions",
        "Property",
        "Sdk",
        "Task",
        "Data",
        "Output",
        "Error",
        "Warning",
        "Message",
        "CallTarget",
        "CreateProperty",
        "CreateItem",
        "RemoveProperty",
        "RemoveItem",
        "Item",
        "None",
        "Compile",
        "Content",
        "EmbeddedResource",
        "Resource",
        "Reference",
        "ProjectReference",
        "PackageReference",
        "FrameworkReference",
        "Analyzer",
        "AdditionalFiles",
        "Folder"
    };

    internal static bool HasCaseInsensitiveDuplicateXmlAttributes(XmlReader reader)
    {
        if (!reader.HasAttributes) return false;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < reader.AttributeCount; index++)
        {
            reader.MoveToAttribute(index);
            if (!names.Add(reader.Name))
            {
                reader.MoveToElement();
                return true;
            }
        }

        reader.MoveToElement();
        return false;
    }

    internal static void ValidateConsumedXmlElementSpelling(
        XmlReader reader,
        IReadOnlyCollection<string> consumedNames,
        string description,
        List<string>? incomplete,
        HashSet<string>? toleratedNames = null)
    {
        if (toleratedNames?.Contains(reader.LocalName) == true) return;

        var canonical = consumedNames.FirstOrDefault(name => reader.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (canonical is null)
        {
            canonical = consumedNames.FirstOrDefault(name => IsNearSpelling(reader.LocalName, name));
            if (canonical is null) return;

            var unknownMessage = $"{description} contains an unknown XML element '{reader.LocalName}' near consumed member '{canonical}'.";
            if (incomplete is null) throw new InvalidOperationException(unknownMessage);
            incomplete.Add(unknownMessage);
            return;
        }

        if (reader.LocalName.Equals(canonical, StringComparison.Ordinal)) return;

        var message = $"{description} contains a non-canonical spelling of consumed XML element '{reader.LocalName}'; expected '{canonical}'.";
        if (incomplete is null)
        {
            throw new InvalidOperationException(message);
        }

        incomplete.Add(message);
    }

    internal static bool IsNearSpelling(string candidate, string canonical)
    {
        if (candidate.Length == 0 || Math.Abs(candidate.Length - canonical.Length) > 2) return false;
        var previous = Enumerable.Range(0, canonical.Length + 1).ToArray();
        for (var row = 1; row <= candidate.Length; row++)
        {
            var current = new int[canonical.Length + 1];
            current[0] = row;
            for (var column = 1; column <= canonical.Length; column++)
            {
                var cost = char.ToUpperInvariant(candidate[row - 1]) == char.ToUpperInvariant(canonical[column - 1]) ? 0 : 1;
                current[column] = Math.Min(
                    Math.Min(current[column - 1] + 1, previous[column] + 1),
                    previous[column - 1] + cost);
            }

            previous = current;
        }

        return previous[canonical.Length] <= 2;
    }

    internal static void ValidateConsumedXmlAttributeSpellings(
        XmlReader reader,
        IReadOnlyCollection<string> allowedNames,
        string description,
        List<string>? incomplete,
        bool rejectUnknown = true)
    {
        if (!reader.HasAttributes) return;
        for (var index = 0; index < reader.AttributeCount; index++)
        {
            reader.MoveToAttribute(index);
            if (IsXmlNamespaceDeclaration(reader) || reader.NamespaceURI.Equals(XmlExtensionNamespace, StringComparison.Ordinal))
            {
                continue;
            }

            var canonical = allowedNames.FirstOrDefault(name => reader.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (canonical is null)
            {
                var near = allowedNames.FirstOrDefault(name => IsNearSpelling(reader.LocalName, name));
                if (near is null && !rejectUnknown)
                {
                    continue;
                }

                var message = near is null
                    ? $"{description} contains an unknown XML attribute '{reader.Name}'."
                    : $"{description} contains an unknown XML attribute '{reader.Name}' near consumed member '{near}'.";
                if (incomplete is null)
                {
                    reader.MoveToElement();
                    throw new InvalidOperationException(message);
                }

                incomplete.Add(message);
                continue;
            }

            if (!reader.LocalName.Equals(canonical, StringComparison.Ordinal))
            {
                var message = $"{description} contains a non-canonical spelling of consumed XML attribute '{reader.Name}'; expected '{canonical}'.";
                if (incomplete is null)
                {
                    reader.MoveToElement();
                    throw new InvalidOperationException(message);
                }

                incomplete.Add(message);
            }
        }

        reader.MoveToElement();
    }

    internal static bool IsXmlNamespaceDeclaration(XmlReader reader) =>
        reader.Prefix.Equals("xmlns", StringComparison.Ordinal) || reader.Name.Equals("xmlns", StringComparison.Ordinal);

    internal static bool IsSupportedMsBuildNamespace(string namespaceUri) =>
        namespaceUri.Length == 0 || namespaceUri.Equals(MsBuildXmlNamespace, StringComparison.Ordinal);

    internal static bool IsDirectProjectChild(MsBuildXmlContext context) =>
        context.Parent is { Name: "Project", Depth: 0 };

    internal static bool IsSupportedImportLocation(MsBuildXmlContext context) =>
        IsDirectProjectChild(context) ||
        (context.Parent is { Name: "ImportGroup", Depth: 1 } && context.Grandparent is { Name: "Project", Depth: 0 });

    internal static bool IsSupportedExecLocation(MsBuildXmlContext context) =>
        context.Parent is { Name: "Target", Depth: 1 } && context.Grandparent is { Name: "Project", Depth: 0 };

    internal static bool IsSupportedInlineCodeLocation(MsBuildXmlContext context) =>
        context.Parent is { Name: "Task", Depth: 2 } &&
        context.Grandparent is { Name: "UsingTask", Depth: 1 } &&
        context.GreatGrandparent is { Name: "Project", Depth: 0 };

    internal static void RejectMsBuildContext(XmlReader reader, MsBuildXmlContext context, List<string>? incomplete, string description)
    {
        var message = $"{description} contains an unsupported MSBuild XML context.";
        if (incomplete is null) throw new InvalidOperationException(message);
        incomplete.Add(message);
        context.IgnoreCurrent(reader);
    }

    internal sealed record MsBuildXmlElement(string Name, int Depth, string? Condition);

    internal sealed class MsBuildXmlContext
    {
        internal readonly List<MsBuildXmlElement> elements = new();
        internal int ignoredDepth = -1;

        public bool IsIgnoring => ignoredDepth >= 0;
        public IReadOnlyList<MsBuildXmlElement> Elements => elements;
        public MsBuildXmlElement? Parent { get; private set; }
        public MsBuildXmlElement? Grandparent { get; private set; }
        public MsBuildXmlElement? GreatGrandparent { get; private set; }

        public bool TryEnter(XmlReader reader, string description, List<string>? incomplete)
        {
            if (IsIgnoring) return false;
            PruneToDepth(reader.Depth);

            Parent = elements.Count == 0 ? null : elements[^1];
            Grandparent = elements.Count < 2 ? null : elements[^2];
            GreatGrandparent = elements.Count < 3 ? null : elements[^3];

            var name = reader.LocalName;
            var namespaceUri = reader.NamespaceURI;
            if (Parent is null)
            {
                if (!name.Equals("Project", StringComparison.Ordinal) || !IsSupportedMsBuildNamespace(namespaceUri))
                {
                    return Reject(reader, description, incomplete);
                }
            }
            else if (name.Equals("ProjectExtensions", StringComparison.Ordinal))
            {
                if (Parent.Name.Equals("Project", StringComparison.Ordinal) && Parent.Depth == 0 &&
                    IsSupportedMsBuildNamespace(namespaceUri))
                {
                    Ignore(reader);
                    return false;
                }

                if (!namespaceUri.Equals(XmlExtensionNamespace, StringComparison.Ordinal))
                {
                    return Reject(reader, description, incomplete);
                }

                Ignore(reader);
                return false;
            }
            else if (namespaceUri.Equals(XmlExtensionNamespace, StringComparison.Ordinal))
            {
                Ignore(reader);
                return false;
            }
            else if (!IsSupportedMsBuildNamespace(namespaceUri))
            {
                return Reject(reader, description, incomplete);
            }

            if (!reader.IsEmptyElement)
            {
                elements.Add(new MsBuildXmlElement(name, reader.Depth, reader.GetAttribute("Condition")));
            }

            return true;
        }

        public void Exit(XmlReader reader)
        {
            if (IsIgnoring)
            {
                if (reader.Depth == ignoredDepth) ignoredDepth = -1;
                return;
            }

            PruneToDepth(reader.Depth);
            Parent = Grandparent = GreatGrandparent = null;
        }

        public void IgnoreCurrent(XmlReader reader)
        {
            PruneToDepth(reader.Depth);
            Parent = Grandparent = GreatGrandparent = null;
            Ignore(reader);
        }

        internal bool Reject(XmlReader reader, string description, List<string>? incomplete)
        {
            var message = $"{description} contains an unsupported MSBuild XML context.";
            if (incomplete is null) throw new InvalidOperationException(message);
            incomplete.Add(message);
            Ignore(reader);
            return false;
        }

        internal void Ignore(XmlReader reader)
        {
            if (!reader.IsEmptyElement) ignoredDepth = reader.Depth;
        }

        internal void PruneToDepth(int depth)
        {
            while (elements.Count > 0 && elements[^1].Depth >= depth)
            {
                elements.RemoveAt(elements.Count - 1);
            }
        }
    }
}
