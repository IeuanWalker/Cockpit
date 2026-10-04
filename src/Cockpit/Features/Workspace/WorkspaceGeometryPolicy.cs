namespace Cockpit.Features.Workspace;

readonly record struct WorkspaceSize(double Width, double Height)
{
	internal static WorkspaceSize Max(WorkspaceSize first, WorkspaceSize second)
		=> new(Math.Max(first.Width, second.Width), Math.Max(first.Height, second.Height));
}

readonly record struct WorkspaceRatioInterval(double Minimum, double Maximum)
{
	internal bool IsEmpty => Minimum > Maximum;
	internal double Clamp(double ratio) => Math.Clamp(ratio, Minimum, Maximum);
}

sealed record WorkspaceGeometryProjection(
	WorkspaceSize MinimumSize,
	bool Fits,
	bool ShouldUseCompactProjection,
	bool ShouldOfferScrollableSurface);

static class WorkspaceGeometryPolicy
{
	internal const double minimumLeafWidth = 220;
	internal const double minimumLeafHeight = 160;
	internal const double dividerThickness = 6;

	internal static WorkspaceSize CalculateMinimumSize(WorkspaceState state)
	{
		WorkspaceValidationResult validation = WorkspaceValidation.Validate(state);
		if(!validation.IsValid)
		{
			throw new ArgumentException(validation.Error, nameof(state));
		}

		Dictionary<string, WorkspaceSize> sizes = new(StringComparer.Ordinal);
		Stack<(string NodeId, bool Visited)> pending = new();
		pending.Push((state.RootNodeId, false));

		while(pending.TryPop(out (string NodeId, bool Visited) item))
		{
			WorkspaceNode node = state.Nodes[item.NodeId];
			if(node is WorkspaceGroupNode)
			{
				sizes[item.NodeId] = new WorkspaceSize(minimumLeafWidth, minimumLeafHeight);
				continue;
			}

			WorkspaceSplitNode split = (WorkspaceSplitNode)node;
			if(!item.Visited)
			{
				pending.Push((item.NodeId, true));
				pending.Push((split.SecondNodeId, false));
				pending.Push((split.FirstNodeId, false));
				continue;
			}

			WorkspaceSize first = sizes[split.FirstNodeId];
			WorkspaceSize second = sizes[split.SecondNodeId];
			sizes[item.NodeId] = split.Axis == WorkspaceSplitAxis.Columns
				? new WorkspaceSize(first.Width + dividerThickness + second.Width, Math.Max(first.Height, second.Height))
				: new WorkspaceSize(Math.Max(first.Width, second.Width), first.Height + dividerThickness + second.Height);
		}

		return sizes[state.RootNodeId];
	}

	internal static WorkspaceRatioInterval GetAllowedRatio(
		WorkspaceState state,
		string splitNodeId,
		WorkspaceSize available)
	{
		if(!state.Nodes.TryGetValue(splitNodeId, out WorkspaceNode? node) || node is not WorkspaceSplitNode split)
		{
			throw new ArgumentException("The requested split does not exist.", nameof(splitNodeId));
		}

		WorkspaceState firstState = SubtreeState(state, split.FirstNodeId);
		WorkspaceState secondState = SubtreeState(state, split.SecondNodeId);
		WorkspaceSize first = CalculateMinimumSize(firstState);
		WorkspaceSize second = CalculateMinimumSize(secondState);
		double extent = split.Axis == WorkspaceSplitAxis.Columns ? available.Width : available.Height;
		double firstMinimum = split.Axis == WorkspaceSplitAxis.Columns ? first.Width : first.Height;
		double secondMinimum = split.Axis == WorkspaceSplitAxis.Columns ? second.Width : second.Height;
		double usable = extent - dividerThickness;
		if(usable <= 0)
		{
			return new WorkspaceRatioInterval(1, 0);
		}
		return new WorkspaceRatioInterval(firstMinimum / usable, 1 - (secondMinimum / usable));
	}

	internal static WorkspaceGeometryProjection Project(WorkspaceState state, WorkspaceSize available)
	{
		WorkspaceSize minimum = CalculateMinimumSize(state);
		bool fits = available.Width >= minimum.Width && available.Height >= minimum.Height;
		return new WorkspaceGeometryProjection(minimum, fits, !fits, !fits);
	}

	static WorkspaceState SubtreeState(WorkspaceState source, string rootNodeId)
	{
		Dictionary<string, WorkspaceNode> nodes = new(StringComparer.Ordinal);
		Dictionary<string, WorkspaceInstance> instances = new(StringComparer.Ordinal);
		Stack<string> pending = new();
		pending.Push(rootNodeId);
		string? firstGroup = null;
		while(pending.TryPop(out string? id))
		{
			WorkspaceNode node = source.Nodes[id];
			nodes[id] = node;
			if(node is WorkspaceGroupNode group)
			{
				firstGroup ??= group.Id;
				foreach(string instanceId in group.OrderedInstanceIds)
				{
					instances[instanceId] = source.Instances[instanceId];
				}
			}
			else if(node is WorkspaceSplitNode split)
			{
				pending.Push(split.SecondNodeId);
				pending.Push(split.FirstNodeId);
			}
		}

		return source with
		{
			RootNodeId = rootNodeId,
			FocusedGroupId = firstGroup!,
			MaximisedGroupId = null,
			Nodes = nodes,
			Instances = instances
		};
	}
}
