namespace Cockpit.Features.Workspace;

sealed record WorkspaceValidationResult(bool IsValid, string? Error = null)
{
	internal static readonly WorkspaceValidationResult valid = new(true);
	internal static WorkspaceValidationResult Invalid(string error) => new(false, error);
}

static class WorkspaceValidation
{
	internal static WorkspaceValidationResult Validate(WorkspaceState state)
	{
		if(!state.Nodes.TryGetValue(state.RootNodeId, out _))
		{
			return WorkspaceValidationResult.Invalid("The root node does not exist.");
		}

		Dictionary<string, string> parentByNode = new(StringComparer.Ordinal);
		HashSet<string> reachable = new(StringComparer.Ordinal);
		Stack<string> pending = new();
		pending.Push(state.RootNodeId);

		while(pending.TryPop(out string? nodeId))
		{
			if(!reachable.Add(nodeId))
			{
				return WorkspaceValidationResult.Invalid($"The layout contains a cycle or duplicate node reference at '{nodeId}'.");
			}

			if(!state.Nodes.TryGetValue(nodeId, out WorkspaceNode? node))
			{
				return WorkspaceValidationResult.Invalid($"The layout references missing node '{nodeId}'.");
			}

			switch(node)
			{
				case WorkspaceGroupNode group:
					HashSet<string> groupInstances = new(StringComparer.Ordinal);
					foreach(string instanceId in group.OrderedInstanceIds)
					{
						if(!groupInstances.Add(instanceId))
						{
							return WorkspaceValidationResult.Invalid($"Group '{group.Id}' contains instance '{instanceId}' more than once.");
						}
						if(!state.Instances.ContainsKey(instanceId))
						{
							return WorkspaceValidationResult.Invalid($"Group '{group.Id}' references missing instance '{instanceId}'.");
						}
					}
					if(group.ActiveInstanceId is not null && !groupInstances.Contains(group.ActiveInstanceId))
					{
						return WorkspaceValidationResult.Invalid($"Group '{group.Id}' has an active instance outside that group.");
					}
					break;
				case WorkspaceSplitNode split:
					if(!double.IsFinite(split.Ratio) || split.Ratio <= 0 || split.Ratio >= 1)
					{
						return WorkspaceValidationResult.Invalid($"Split '{split.Id}' has an invalid ratio.");
					}
					if(split.FirstNodeId == split.SecondNodeId)
					{
						return WorkspaceValidationResult.Invalid($"Split '{split.Id}' references the same child twice.");
					}
					foreach(string childId in new[] { split.FirstNodeId, split.SecondNodeId })
					{
						if(parentByNode.TryGetValue(childId, out string? parent))
						{
							return WorkspaceValidationResult.Invalid($"Node '{childId}' has multiple parents ('{parent}' and '{split.Id}').");
						}
						parentByNode[childId] = split.Id;
						pending.Push(childId);
					}
					break;
				default:
					return WorkspaceValidationResult.Invalid($"Node '{node.Id}' has an unsupported kind.");
			}
		}

		if(reachable.Count != state.Nodes.Count)
		{
			return WorkspaceValidationResult.Invalid("The layout contains unreachable nodes.");
		}

		HashSet<string> placedInstances = new(StringComparer.Ordinal);
		foreach(WorkspaceGroupNode group in state.Nodes.Values.OfType<WorkspaceGroupNode>())
		{
			foreach(string instanceId in group.OrderedInstanceIds)
			{
				if(!placedInstances.Add(instanceId))
				{
					return WorkspaceValidationResult.Invalid($"Instance '{instanceId}' appears in more than one group.");
				}
			}
		}

		if(placedInstances.Count != state.Instances.Count)
		{
			return WorkspaceValidationResult.Invalid("One or more instances are not placed in the layout.");
		}
		if(!state.Nodes.TryGetValue(state.FocusedGroupId, out WorkspaceNode? focused) || focused is not WorkspaceGroupNode)
		{
			return WorkspaceValidationResult.Invalid("The focused group does not exist.");
		}
		if(state.MaximisedGroupId is not null && (!state.Nodes.TryGetValue(state.MaximisedGroupId, out WorkspaceNode? maximised) || maximised is not WorkspaceGroupNode))
		{
			return WorkspaceValidationResult.Invalid("The maximised group does not exist.");
		}

		return WorkspaceValidationResult.valid;
	}
}
