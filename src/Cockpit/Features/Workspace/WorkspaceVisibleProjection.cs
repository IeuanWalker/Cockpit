namespace Cockpit.Features.Workspace;

sealed record VisibleWorkspaceProjection(
	string RootNodeId,
	string FocusedGroupId,
	string? MaximisedGroupId,
	IReadOnlyDictionary<string, WorkspaceNode> Nodes,
	IReadOnlyDictionary<string, WorkspaceInstance> Instances,
	IReadOnlySet<string> DormantInstanceIds);

static class WorkspaceVisibleProjection
{
	internal static VisibleWorkspaceProjection Create(WorkspaceState canonical)
	{
		WorkspaceValidationResult validation = WorkspaceValidation.Validate(canonical);
		if(!validation.IsValid)
		{
			throw new ArgumentException(validation.Error, nameof(canonical));
		}

		HashSet<string> dormant = new(StringComparer.Ordinal);
		Dictionary<string, WorkspaceInstance> visibleInstances = new(StringComparer.Ordinal);
		Dictionary<string, WorkspaceNode> nodes = new(StringComparer.Ordinal);

		foreach((string nodeId, WorkspaceNode node) in canonical.Nodes)
		{
			if(node is not WorkspaceGroupNode group)
			{
				nodes[nodeId] = node;
				continue;
			}

			List<string> visibleIds = [];
			foreach(string instanceId in group.OrderedInstanceIds)
			{
				WorkspaceInstance instance = canonical.Instances[instanceId];
				WorkspaceToolAvailability status = canonical.Availability.TryGetValue(instance.ToolId, out WorkspaceAvailabilityState? availability)
					? availability.Status
					: WorkspaceToolAvailability.Missing;

				// Missing definitions remain visible as explicit recovery entries. A registered tool that
				// says it is unavailable (or whose evaluation failed) stays dormant in canonical state.
				if(status is WorkspaceToolAvailability.Available or WorkspaceToolAvailability.Missing)
				{
					visibleIds.Add(instanceId);
					visibleInstances[instanceId] = instance;
				}
				else
				{
					dormant.Add(instanceId);
				}
			}

			string? active = group.ActiveInstanceId is not null && visibleIds.Contains(group.ActiveInstanceId, StringComparer.Ordinal)
				? group.ActiveInstanceId
				: visibleIds.FirstOrDefault();
			nodes[nodeId] = group with { OrderedInstanceIds = visibleIds, ActiveInstanceId = active };
		}

		// Groups are deliberately retained even when every child instance is dormant. This keeps
		// canonical drop locations stable and leaves an add action in each user-created group.
		string focused = canonical.FocusedGroupId;
		string? maximised = canonical.MaximisedGroupId;
		return new VisibleWorkspaceProjection(
			canonical.RootNodeId,
			focused,
			maximised,
			nodes,
			visibleInstances,
			dormant);
	}
}
