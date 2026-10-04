using System.Text.Json;
using Cockpit.Tooling.Discovery.Build;
using Microsoft.Build.Utilities;

namespace Cockpit.Tooling.Tests;

public sealed class WorkspaceToolCatalogBuildTests
{
	[Fact]
	public void Build_task_follows_managed_references_and_writes_deterministic_catalogue()
	{
		string outputDirectory = Path.Combine(Path.GetTempPath(), "cockpit-tool-catalog-tests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(outputDirectory);
		try
		{
			GenerateWorkspaceToolCatalog task = new()
			{
				Assemblies =
				[
					new TaskItem(typeof(IWorkspaceTool).Assembly.Location),
					new TaskItem(typeof(WorkspaceToolCatalogBuildTests).Assembly.Location)
				],
				CatalogPath = Path.Combine(outputDirectory, "workspace-tool-assemblies.json"),
				LinkerDescriptorPath = Path.Combine(outputDirectory, "workspace-tool-linker.xml")
			};

			Assert.True(task.Execute());

			using JsonDocument document = JsonDocument.Parse(File.ReadAllText(task.CatalogPath));
			string[] assemblies = [.. document.RootElement.GetProperty("assemblies")
				.EnumerateArray()
				.Select(item => item.GetString()!)];
			string expectedAssemblyName = typeof(WorkspaceToolCatalogBuildTests).Assembly.GetName().Name!;
			Assert.Single(assemblies);
			Assert.Equal(expectedAssemblyName, assemblies[0]);
			Assert.Contains(expectedAssemblyName, File.ReadAllText(task.LinkerDescriptorPath));
		}
		finally
		{
			Directory.Delete(outputDirectory, recursive: true);
		}
	}
}
