using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Cockpit.Tooling.Discovery.Build;

public sealed class GenerateWorkspaceToolCatalog : Microsoft.Build.Utilities.Task
{
	[Required]
	public ITaskItem[] Assemblies { get; set; } = [];

	[Required]
	public string ContractAssemblyName { get; set; } = "Cockpit.Tooling.Abstractions";

	[Required]
	public string CatalogPath { get; set; } = string.Empty;

	[Required]
	public string LinkerDescriptorPath { get; set; } = string.Empty;

	[Output]
	public ITaskItem[] ToolAssemblies { get; private set; } = [];

	public override bool Execute()
	{
		try
		{
			Dictionary<string, AssemblyCandidate> candidates = ReadCandidates();
			AssemblyCandidate[] selected = [.. candidates.Values
				.Where(candidate => !string.Equals(candidate.Name, ContractAssemblyName, StringComparison.Ordinal))
				.Where(candidate => ReferencesContract(candidate.Name, candidates, []))
				.OrderBy(candidate => candidate.Name, StringComparer.Ordinal)];

			Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(CatalogPath))!);
			using(FileStream stream = File.Create(CatalogPath))
			{
				JsonSerializer.Serialize(stream, new
				{
					version = 1,
					assemblies = selected.Select(item => item.Name).ToArray()
				});
			}

			Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(LinkerDescriptorPath))!);
			File.WriteAllText(
				LinkerDescriptorPath,
				"<linker>" + string.Concat(selected.Select(item =>
					$"<assembly fullname=\"{System.Security.SecurityElement.Escape(item.Name)}\" preserve=\"all\" />")) + "</linker>");

			ToolAssemblies = [.. selected.Select(item => (ITaskItem)new TaskItem(item.Path))];
			return true;
		}
		catch(Exception exception)
		{
			Log.LogErrorFromException(exception, showStackTrace: true);
			return false;
		}
	}

	Dictionary<string, AssemblyCandidate> ReadCandidates()
	{
		Dictionary<string, AssemblyCandidate> candidates = new(StringComparer.Ordinal);
		foreach(ITaskItem? item in Assemblies.OrderBy(item => item.ItemSpec, StringComparer.Ordinal))
		{
			string path = Path.GetFullPath(item.ItemSpec);
			if(!File.Exists(path) || !string.Equals(Path.GetExtension(path), ".dll", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			try
			{
				using FileStream stream = File.OpenRead(path);
				using PEReader peReader = new(stream, PEStreamOptions.LeaveOpen);
				if(!peReader.HasMetadata)
				{
					continue;
				}

				MetadataReader metadata = peReader.GetMetadataReader();
				if(!metadata.IsAssembly)
				{
					continue;
				}

				AssemblyDefinition definition = metadata.GetAssemblyDefinition();
				string name = metadata.GetString(definition.Name);
				HashSet<string> references = metadata.AssemblyReferences
					.Select(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name))
					.ToHashSet(StringComparer.Ordinal);
				candidates.TryAdd(name, new(name, path, references));
			}
			catch(BadImageFormatException)
			{
				// Native/runtime assets are not managed catalogue candidates.
			}
		}
		return candidates;
	}

	bool ReferencesContract(
		string name,
		IReadOnlyDictionary<string, AssemblyCandidate> candidates,
		HashSet<string> visited)
	{
		if(!visited.Add(name) || !candidates.TryGetValue(name, out AssemblyCandidate? candidate))
		{
			return false;
		}

		if(candidate.References.Contains(ContractAssemblyName))
		{
			return true;
		}

		return candidate.References.Any(reference => ReferencesContract(reference, candidates, visited));
	}

	sealed record AssemblyCandidate(string Name, string Path, IReadOnlySet<string> References);
}
