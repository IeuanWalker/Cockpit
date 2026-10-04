using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cockpit.Tooling.Discovery;

public interface IWorkspaceToolAssemblyCatalog
{
	IReadOnlyList<string> AssemblyNames { get; }
}

public sealed record WorkspaceToolAssemblyCatalog(IReadOnlyList<string> AssemblyNames)
	: IWorkspaceToolAssemblyCatalog
{
	public const string ResourceSuffix = "workspace-tool-assemblies.json";

	public static WorkspaceToolAssemblyCatalog FromEmbeddedResource(Assembly hostAssembly)
	{
		ArgumentNullException.ThrowIfNull(hostAssembly);
		string[] resources = [.. hostAssembly.GetManifestResourceNames()
			.Where(name => name.EndsWith(ResourceSuffix, StringComparison.OrdinalIgnoreCase))
			.Order(StringComparer.Ordinal)];

		if(resources.Length != 1)
		{
			throw new InvalidOperationException(
				$"Expected exactly one embedded {ResourceSuffix} resource in {hostAssembly.GetName().Name}, found {resources.Length}.");
		}

		using Stream stream = hostAssembly.GetManifestResourceStream(resources[0])
			?? throw new InvalidOperationException($"Could not open embedded resource {resources[0]}.");
		WorkspaceToolCatalogDocument document = JsonSerializer.Deserialize(stream, WorkspaceToolDiscoveryJsonContext.Default.WorkspaceToolCatalogDocument)
			?? throw new InvalidDataException("The workspace tool catalogue is empty.");
		if(document.Version != 1)
		{
			throw new NotSupportedException($"Workspace tool catalogue version {document.Version} is not supported.");
		}

		string[] names = [.. document.Assemblies
			.Where(name => !string.IsNullOrWhiteSpace(name))
			.Distinct(StringComparer.Ordinal)
			.Order(StringComparer.Ordinal)];
		return new WorkspaceToolAssemblyCatalog(names);
	}
}

sealed record WorkspaceToolCatalogDocument(
	[property: JsonPropertyName("version")] int Version,
	[property: JsonPropertyName("assemblies")] string[] Assemblies);
