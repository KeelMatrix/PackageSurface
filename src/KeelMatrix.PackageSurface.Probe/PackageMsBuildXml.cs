using System.Xml;
using static KeelMatrix.PackageSurface.Probe.AnalysisLimits;
using static KeelMatrix.PackageSurface.Probe.CapabilityAssetClassifier;
using static KeelMatrix.PackageSurface.Probe.MsBuildXmlSchema;

namespace KeelMatrix.PackageSurface.Probe;

internal static class PackageMsBuildXml
{
    internal static IReadOnlyList<string>? InspectPackageBuildMetadata(
        CapabilityKind capability,
        bool present,
        string physicalPath,
        string relativePath,
        string libraryKey,
        List<string> incomplete,
        ref string? reason)
    {
        if (!present || !IsBuildCapability(capability))
        {
            return null;
        }

        try
        {
            return InspectMsBuildXml(physicalPath);
        }
        catch (XmlException)
        {
            incomplete.Add($"{libraryKey}: package MSBuild file {relativePath} is malformed.");
            reason = $"Package MSBuild XML is malformed: {relativePath}.";
            return null;
        }
        catch (InvalidOperationException)
        {
            incomplete.Add($"{libraryKey}: package MSBuild file {relativePath} is unsupported.");
            reason = $"Package MSBuild XML is unsupported: {relativePath}.";
            return null;
        }
    }

    internal static IReadOnlyList<string>? InspectNestedBuildMetadata(
        string physicalPath,
        string libraryKey,
        string relativePath,
        AnalysisBudget budget,
        List<string> incomplete)
    {
        budget.AddOperation("nested surface entry");
        try
        {
            return InspectMsBuildXml(physicalPath);
        }
        catch (XmlException)
        {
            incomplete.Add($"{libraryKey}: nested package MSBuild file {relativePath} is malformed.");
            return null;
        }
        catch (InvalidOperationException)
        {
            incomplete.Add($"{libraryKey}: nested package MSBuild file {relativePath} is unsupported.");
            return null;
        }
    }

    internal static string[] InspectMsBuildXml(string path)
    {
        var observations = new HashSet<string>(StringComparer.Ordinal);
        var inlineTasks = new Stack<InlineTaskState>();
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersFromEntities = 0,
            MaxCharactersInDocument = MaxXmlCharacters,
            IgnoreComments = true,
            IgnoreWhitespace = true
        };

        using var reader = XmlReader.Create(path, settings);
        var xmlContext = new MsBuildXmlContext();
        while (reader.Read())
        {
            if (reader.Depth > MaxXmlDepth) throw new InvalidOperationException("XML depth exceeds the supported limit.");
            if (reader.NodeType == XmlNodeType.EndElement)
            {
                if (!xmlContext.IsIgnoring && reader.LocalName.Equals("UsingTask", StringComparison.Ordinal) &&
                    IsSupportedMsBuildNamespace(reader.NamespaceURI) && inlineTasks.Count > 0 &&
                    inlineTasks.Peek().Depth == reader.Depth)
                {
                    var task = inlineTasks.Pop();
                    if (task.IsInlineFactory && task.HasCodeElement) observations.Add("InlineTaskFactory");
                }

                xmlContext.Exit(reader);
                continue;
            }

            if (reader.NodeType != XmlNodeType.Element) continue;
            if (!xmlContext.TryEnter(reader, "package MSBuild XML", incomplete: null)) continue;
            ValidateConsumedXmlElementSpelling(
                reader,
                PackageMsBuildElementNames,
                "package MSBuild XML",
                null,
                PackageMsBuildStandardElementNames);
            ValidateConsumedXmlAttributeSpellings(
                reader,
                PackageMsBuildAttributeNames,
                "package MSBuild XML",
                null,
                rejectUnknown: false);
            var elementName = reader.LocalName;
            if (elementName.Equals("ImportGroup", StringComparison.Ordinal) && !IsDirectProjectChild(xmlContext))
            {
                RejectMsBuildContext(reader, xmlContext, incomplete: null, "package MSBuild XML");
            }

            switch (elementName)
            {
                case "UsingTask":
                    if (!IsDirectProjectChild(xmlContext))
                    {
                        RejectMsBuildContext(reader, xmlContext, incomplete: null, "package MSBuild XML");
                    }
                    observations.Add("UsingTask");
                    var factory = reader.GetAttribute("TaskFactory");
                    if (!reader.IsEmptyElement)
                    {
                        inlineTasks.Push(new InlineTaskState(reader.Depth,
                            factory is not null && (factory.Equals("CodeTaskFactory", StringComparison.OrdinalIgnoreCase) || factory.Equals("RoslynCodeTaskFactory", StringComparison.OrdinalIgnoreCase)), false));
                    }
                    break;
                case "Code":
                    if (!IsSupportedInlineCodeLocation(xmlContext) || inlineTasks.Count == 0 || reader.Depth <= inlineTasks.Peek().Depth)
                    {
                        RejectMsBuildContext(reader, xmlContext, incomplete: null, "package MSBuild XML");
                    }
                    var task = inlineTasks.Pop();
                    inlineTasks.Push(task with { HasCodeElement = true });
                    break;
                case "Exec":
                    if (!IsSupportedExecLocation(xmlContext))
                    {
                        RejectMsBuildContext(reader, xmlContext, incomplete: null, "package MSBuild XML");
                    }
                    observations.Add("Exec");
                    break;
                case "Import":
                    if (!IsSupportedImportLocation(xmlContext) || reader.GetAttribute("Project") is null)
                    {
                        RejectMsBuildContext(reader, xmlContext, incomplete: null, "package MSBuild XML");
                    }
                    observations.Add("Import");
                    break;
            }
        }

        return observations.Order(StringComparer.Ordinal).ToArray();
    }

    internal sealed record InlineTaskState(int Depth, bool IsInlineFactory, bool HasCodeElement);
}
