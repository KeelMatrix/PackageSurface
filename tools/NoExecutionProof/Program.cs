using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: no-execution-proof <classifier.dll>");
    return 2;
}

using var stream = File.OpenRead(args[0]);
using var pe = new PEReader(stream);
var metadata = pe.GetMetadataReader();
var forbiddenAssemblies = new[] { "Microsoft.Build", "System.Diagnostics.Process", "System.Net.Http", "System.Net.Sockets", "System.Net.NetworkInformation" };
var forbiddenTypes = new[] { "System.Diagnostics.Process", "System.Reflection.Assembly", "System.Runtime.Loader.AssemblyLoadContext", "System.Net.Http.HttpClient", "System.Net.WebRequest" };

var assemblyViolations = metadata.AssemblyReferences
    .Select(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name))
    .Where(name => forbiddenAssemblies.Any(forbidden => name.Equals(forbidden, StringComparison.OrdinalIgnoreCase)))
    .ToArray();

var memberViolations = new List<string>();
foreach (var handle in metadata.MemberReferences)
{
    var member = metadata.GetMemberReference(handle);
    var typeName = GetTypeName(metadata, member.Parent);
    var memberName = metadata.GetString(member.Name);
    if (typeName is not null && forbiddenTypes.Contains(typeName, StringComparer.Ordinal) &&
        (typeName != "System.Reflection.Assembly" || memberName is "Load" or "LoadFrom" or "LoadFile" or "UnsafeLoadFrom") &&
        (typeName != "System.Diagnostics.Process" || memberName == "Start") &&
        (typeName != "System.Net.Http.HttpClient" || memberName is "Send" or "SendAsync" or "GetAsync" or "GetStringAsync"))
    {
        memberViolations.Add(typeName + "." + memberName);
    }
}

if (assemblyViolations.Length != 0 || memberViolations.Count != 0)
{
    Console.Error.WriteLine("Forbidden references detected.");
    Console.Error.WriteLine(string.Join(Environment.NewLine, assemblyViolations.Concat(memberViolations)));
    return 1;
}

Console.WriteLine("PASS: classifier assembly has no forbidden assembly, process-start, assembly-load, MSBuild, or network references.");
return 0;

static string? GetTypeName(MetadataReader reader, EntityHandle handle)
{
    return handle.Kind switch
    {
        HandleKind.TypeReference => GetTypeReferenceName(reader, reader.GetTypeReference((TypeReferenceHandle)handle)),
        HandleKind.TypeDefinition => GetTypeDefinitionName(reader, reader.GetTypeDefinition((TypeDefinitionHandle)handle)),
        HandleKind.TypeSpecification => null,
        _ => null
    };
}

static string GetTypeReferenceName(MetadataReader reader, TypeReference reference)
{
    var ns = reader.GetString(reference.Namespace);
    var name = reader.GetString(reference.Name);
    return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
}

static string GetTypeDefinitionName(MetadataReader reader, TypeDefinition definition)
{
    var ns = reader.GetString(definition.Namespace);
    var name = reader.GetString(definition.Name);
    return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
}
