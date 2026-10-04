using Cockpit.Tooling.Discovery;

namespace Cockpit.Tooling.Tests;

public sealed class WorkspaceToolAssemblyCatalogTests
{
	[Fact]
	public void Reads_generated_lowercase_catalogue_from_host_resource()
	{
		WorkspaceToolAssemblyCatalog catalog = WorkspaceToolAssemblyCatalog.FromEmbeddedResource(
			typeof(WorkspaceToolAssemblyCatalogTests).Assembly);

		Assert.Equal(["Cockpit.Tools.Sample"], catalog.AssemblyNames);
	}
}
