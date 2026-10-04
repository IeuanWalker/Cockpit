namespace Cockpit.Features.Workspace;

internal static class WorkspaceLayoutPolicy
{
	internal const int MaxSections = 4;

	internal static bool CanCreateSection(WorkspaceState state) =>
		state.Nodes.Values.Count(node => node is WorkspaceGroupNode) < MaxSections;

	internal static bool CanSplitExistingInstance(WorkspaceState state, string instanceId, string targetGroupId)
	{
		WorkspaceGroupNode[] groups = [.. state.Nodes.Values.OfType<WorkspaceGroupNode>()];
		if(groups.Length < MaxSections)
		{
			return true;
		}

		WorkspaceGroupNode? source = groups.FirstOrDefault(group => group.OrderedInstanceIds.Contains(instanceId, StringComparer.Ordinal));
		return source is not null && source.Id != targetGroupId && source.OrderedInstanceIds.Count == 1;
	}

	internal static bool CanSplitRootWithExistingInstance(WorkspaceState state, string instanceId)
	{
		WorkspaceGroupNode[] groups = [.. state.Nodes.Values.OfType<WorkspaceGroupNode>()];
		if(groups.Length < MaxSections)
		{
			return true;
		}

		WorkspaceGroupNode? source = groups.FirstOrDefault(group => group.OrderedInstanceIds.Contains(instanceId, StringComparer.Ordinal));
		return source is not null && groups.Length > 1 && source.OrderedInstanceIds.Count == 1;
	}
}
