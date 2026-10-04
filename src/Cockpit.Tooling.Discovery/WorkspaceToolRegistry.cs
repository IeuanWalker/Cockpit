using Microsoft.Extensions.DependencyInjection;

namespace Cockpit.Tooling.Discovery;

public sealed record RegisteredWorkspaceTool(
	WorkspaceToolDescriptor Descriptor,
	IWorkspaceTool Definition,
	Type DefinitionType);

public sealed class WorkspaceToolRegistry
{
	readonly IReadOnlyDictionary<string, RegisteredWorkspaceTool> _byId;

	internal WorkspaceToolRegistry(
		IServiceProvider services,
		IReadOnlyList<Type> definitionTypes,
		WorkspaceToolDiscoveryDiagnostics diagnostics)
	{
		List<RegisteredWorkspaceTool> candidates = [];
		foreach(Type definitionType in definitionTypes)
		{
			try
			{
				IWorkspaceTool definition = (IWorkspaceTool)services.GetRequiredService(definitionType);
				IReadOnlyList<string> errors = definition.Descriptor.Validate();
				if(errors.Count != 0)
				{
					diagnostics.Add(new(
						WorkspaceToolDiscoveryDiagnosticKind.DescriptorInvalid,
						definitionType.FullName ?? definitionType.Name,
						string.Join(" ", errors)));
					continue;
				}
				candidates.Add(new(definition.Descriptor, definition, definitionType));
			}
			catch(Exception exception)
			{
				diagnostics.Add(new(
					WorkspaceToolDiscoveryDiagnosticKind.DefinitionFailed,
					definitionType.FullName ?? definitionType.Name,
					exception.Message,
					exception));
			}
		}

		HashSet<string> rejected = FindConflictingToolIds(candidates);
		foreach(string? conflict in rejected.Order(StringComparer.Ordinal))
		{
			diagnostics.Add(new(
				WorkspaceToolDiscoveryDiagnosticKind.IdentityConflict,
				conflict,
				$"Tool identifier or alias '{conflict}' is claimed by more than one definition."));
		}

		RegisteredWorkspaceTool[] accepted = [.. candidates
			.Where(candidate => CandidateHasNoConflict(candidate, rejected))
			.OrderBy(candidate => candidate.Descriptor.ToolId, StringComparer.Ordinal)];
		_byId = accepted
			.SelectMany(item => item.Descriptor.FormerToolIds
				.Prepend(item.Descriptor.ToolId)
				.Select(id => KeyValuePair.Create(id, item)))
			.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
		Tools = accepted;
	}

	public IReadOnlyList<RegisteredWorkspaceTool> Tools { get; }

	public bool TryGet(string toolId, out RegisteredWorkspaceTool? tool) =>
		_byId.TryGetValue(toolId, out tool);

	static HashSet<string> FindConflictingToolIds(IReadOnlyList<RegisteredWorkspaceTool> candidates)
	{
		Dictionary<string, HashSet<string>> owners = new(StringComparer.Ordinal);
		foreach(RegisteredWorkspaceTool candidate in candidates)
		{
			foreach(string? id in candidate.Descriptor.FormerToolIds.Prepend(candidate.Descriptor.ToolId))
			{
				if(!owners.TryGetValue(id, out HashSet<string>? toolIds))
				{
					owners[id] = toolIds = new(StringComparer.Ordinal);
				}

				toolIds.Add(candidate.Descriptor.ToolId);
			}
		}
		return owners.Where(pair => pair.Value.Count > 1).Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal);
	}

	static bool CandidateHasNoConflict(RegisteredWorkspaceTool candidate, IReadOnlySet<string> conflicts) =>
		!conflicts.Contains(candidate.Descriptor.ToolId) &&
		!candidate.Descriptor.FormerToolIds.Any(conflicts.Contains);
}
