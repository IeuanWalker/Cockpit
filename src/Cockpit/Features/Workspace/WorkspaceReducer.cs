namespace Cockpit.Features.Workspace;

static class WorkspaceReducer
{
	internal static WorkspaceChangeResult OpenNewTool(
		WorkspaceState state,
		string groupId,
		WorkspaceInstance instance,
		int? insertionIndex = null,
		long? expectedLayoutRevision = null)
	{
		if(IsStale(state, expectedLayoutRevision))
		{
			return RejectStale(state);
		}
		if(state.Instances.ContainsKey(instance.Id) || state.Nodes.ContainsKey(instance.Id))
		{
			return WorkspaceChangeResult.Rejected(state, WorkspaceChangeError.DuplicateId);
		}
		if(!TryGetGroup(state, groupId, out WorkspaceGroupNode? group))
		{
			return WorkspaceChangeResult.Rejected(state, WorkspaceChangeError.GroupNotFound);
		}

		List<string> ids = [.. group.OrderedInstanceIds];
		int index = insertionIndex ?? ids.Count;
		if(index < 0 || index > ids.Count)
		{
			return WorkspaceChangeResult.Rejected(state, WorkspaceChangeError.InvalidIndex);
		}
		ids.Insert(index, instance.Id);

		Dictionary<string, WorkspaceNode> nodes = CopyNodes(state);
		nodes[groupId] = group with { OrderedInstanceIds = ids, ActiveInstanceId = instance.Id };
		Dictionary<string, WorkspaceInstance> instances = CopyInstances(state);
		instances.Add(instance.Id, instance);
		return CommitLayout(state, nodes, instances, focusedGroupId: groupId);
	}

	internal static WorkspaceChangeResult ActivateInstance(WorkspaceState state, string groupId, string instanceId)
	{
		if(!TryGetGroup(state, groupId, out WorkspaceGroupNode? group))
		{
			return WorkspaceChangeResult.Rejected(state, WorkspaceChangeError.GroupNotFound);
		}
		if(!group.OrderedInstanceIds.Contains(instanceId, StringComparer.Ordinal))
		{
			return WorkspaceChangeResult.Rejected(state, WorkspaceChangeError.InstanceNotFound);
		}
		if(group.ActiveInstanceId == instanceId && state.FocusedGroupId == groupId)
		{
			return WorkspaceChangeResult.NoChange(state);
		}

		Dictionary<string, WorkspaceNode> nodes = CopyNodes(state);
		nodes[groupId] = group with { ActiveInstanceId = instanceId };
		return CommitLayout(state, nodes, state.Instances, focusedGroupId: groupId);
	}

	internal static WorkspaceChangeResult CloseInstance(WorkspaceState state, string instanceId, long? expectedLayoutRevision = null)
	{
		if(IsStale(state, expectedLayoutRevision))
		{
			return RejectStale(state);
		}
		if(!TryFindInstanceGroup(state, instanceId, out WorkspaceGroupNode? group))
		{
			return WorkspaceChangeResult.Rejected(state, WorkspaceChangeError.InstanceNotFound);
		}

		Dictionary<string, WorkspaceNode> nodes = CopyNodes(state);
		Dictionary<string, WorkspaceInstance> instances = CopyInstances(state);
		List<string> ids = [.. group.OrderedInstanceIds];
		int removedIndex = ids.IndexOf(instanceId);
		ids.RemoveAt(removedIndex);
		string? active = group.ActiveInstanceId == instanceId
			? ids.ElementAtOrDefault(Math.Min(removedIndex, ids.Count - 1))
			: group.ActiveInstanceId;
		nodes[group.Id] = group with { OrderedInstanceIds = ids, ActiveInstanceId = active };
		instances.Remove(instanceId);

		string rootId = state.RootNodeId;
		string focused = state.FocusedGroupId;
		string? maximised = state.MaximisedGroupId;
		if(ids.Count == 0 && nodes.Count > 1)
		{
			CollapseEmptyGroup(nodes, group.Id, ref rootId, ref focused, ref maximised);
		}

		return CommitLayout(state, nodes, instances, rootId, focused, maximised, setMaximised: true);
	}

	internal static WorkspaceChangeResult MoveInstance(
		WorkspaceState state,
		string instanceId,
		string destinationGroupId,
		int destinationIndex,
		long? expectedLayoutRevision = null)
	{
		if(IsStale(state, expectedLayoutRevision))
		{
			return RejectStale(state);
		}
		if(!TryFindInstanceGroup(state, instanceId, out WorkspaceGroupNode? source)
			|| !TryGetGroup(state, destinationGroupId, out WorkspaceGroupNode? destination))
		{
			return WorkspaceChangeResult.Rejected(state, WorkspaceChangeError.InvalidMove);
		}

		List<string> sourceIds = [.. source.OrderedInstanceIds];
		List<string> destinationIds = source.Id == destination.Id ? sourceIds : [.. destination.OrderedInstanceIds];
		sourceIds.Remove(instanceId);
		if(destinationIndex < 0 || destinationIndex > destinationIds.Count)
		{
			return WorkspaceChangeResult.Rejected(state, WorkspaceChangeError.InvalidIndex);
		}
		if(source.Id == destination.Id)
		{
			destinationIds.Insert(destinationIndex, instanceId);
			if(destinationIds.SequenceEqual(source.OrderedInstanceIds, StringComparer.Ordinal))
			{
				return WorkspaceChangeResult.NoChange(state);
			}
		}
		else
		{
			destinationIds.Insert(destinationIndex, instanceId);
		}

		Dictionary<string, WorkspaceNode> nodes = CopyNodes(state);
		nodes[source.Id] = source with
		{
			OrderedInstanceIds = sourceIds,
			ActiveInstanceId = SelectActiveAfterRemoval(source, sourceIds, instanceId)
		};
		nodes[destination.Id] = destination with { OrderedInstanceIds = destinationIds, ActiveInstanceId = instanceId };

		string rootId = state.RootNodeId;
		string focused = destination.Id;
		string? maximised = state.MaximisedGroupId;
		if(source.Id != destination.Id && sourceIds.Count == 0)
		{
			CollapseEmptyGroup(nodes, source.Id, ref rootId, ref focused, ref maximised);
		}
		return CommitLayout(state, nodes, state.Instances, rootId, focused, maximised, setMaximised: true);
	}

	internal static WorkspaceChangeResult SplitGroupWithInstance(
		WorkspaceState state,
		string sourceGroupId,
		WorkspaceInstance instance,
		string newGroupId,
		string splitNodeId,
		WorkspaceSplitAxis axis,
		bool placeNewGroupFirst,
		double ratio = 0.5,
		long? expectedLayoutRevision = null)
	{
		if(IsStale(state, expectedLayoutRevision))
		{
			return RejectStale(state);
		}
		if(!WorkspaceLayoutPolicy.CanCreateSection(state))
		{
			return WorkspaceChangeResult.Rejected(state, WorkspaceChangeError.SectionLimitReached, $"A workspace can contain at most {WorkspaceLayoutPolicy.MaxSections} sections.");
		}
		if(!TryGetGroup(state, sourceGroupId, out _))
		{
			return WorkspaceChangeResult.Rejected(state, WorkspaceChangeError.GroupNotFound);
		}
		if(state.Instances.ContainsKey(instance.Id)
			|| state.Nodes.ContainsKey(instance.Id)
			|| state.Nodes.ContainsKey(newGroupId)
			|| state.Nodes.ContainsKey(splitNodeId)
			|| newGroupId == splitNodeId)
		{
			return WorkspaceChangeResult.Rejected(state, WorkspaceChangeError.DuplicateId);
		}
		if(!double.IsFinite(ratio) || ratio <= 0 || ratio >= 1)
		{
			return WorkspaceChangeResult.Rejected(state, WorkspaceChangeError.InvalidRatio);
		}

		Dictionary<string, WorkspaceNode> nodes = CopyNodes(state);
		WorkspaceGroupNode newGroup = new(newGroupId, [instance.Id], instance.Id);
		WorkspaceSplitNode split = placeNewGroupFirst
			? new WorkspaceSplitNode(splitNodeId, axis, ratio, newGroupId, sourceGroupId)
			: new WorkspaceSplitNode(splitNodeId, axis, ratio, sourceGroupId, newGroupId);
		ReplaceNodeReference(nodes, state.RootNodeId, sourceGroupId, splitNodeId, out string rootId);
		nodes[newGroupId] = newGroup;
		nodes[splitNodeId] = split;
		Dictionary<string, WorkspaceInstance> instances = CopyInstances(state);
		instances.Add(instance.Id, instance);
		return CommitLayout(state, nodes, instances, rootId, newGroupId, maximisedGroupId: null, setMaximised: true);
	}

	internal static WorkspaceChangeResult SplitGroupWithExistingInstance(
		WorkspaceState state,
		string instanceId,
		string targetGroupId,
		string newGroupId,
		string splitNodeId,
		WorkspaceSplitAxis axis,
		bool placeNewGroupFirst,
		double ratio = 0.5,
		long? expectedLayoutRevision = null)
	{
		if(IsStale(state, expectedLayoutRevision))
		{
			return RejectStale(state);
		}
		if(!WorkspaceLayoutPolicy.CanSplitExistingInstance(state, instanceId, targetGroupId))
		{
			return WorkspaceChangeResult.Rejected(state, WorkspaceChangeError.SectionLimitReached, $"A workspace can contain at most {WorkspaceLayoutPolicy.MaxSections} sections.");
		}
		if(!TryFindInstanceGroup(state, instanceId, out WorkspaceGroupNode? source)
			|| !TryGetGroup(state, targetGroupId, out _))
		{
			return WorkspaceChangeResult.Rejected(state, WorkspaceChangeError.InvalidMove);
		}
		if(source.Id == targetGroupId && source.OrderedInstanceIds.Count == 1)
		{
			return WorkspaceChangeResult.Rejected(state, WorkspaceChangeError.InvalidMove, "Moving the only tab cannot produce two useful groups.");
		}
		if(state.Nodes.ContainsKey(newGroupId) || state.Nodes.ContainsKey(splitNodeId) || newGroupId == splitNodeId)
		{
			return WorkspaceChangeResult.Rejected(state, WorkspaceChangeError.DuplicateId);
		}
		if(!double.IsFinite(ratio) || ratio <= 0 || ratio >= 1)
		{
			return WorkspaceChangeResult.Rejected(state, WorkspaceChangeError.InvalidRatio);
		}

		Dictionary<string, WorkspaceNode> nodes = CopyNodes(state);
		List<string> sourceIds = [.. source.OrderedInstanceIds];
		sourceIds.Remove(instanceId);
		nodes[source.Id] = source with
		{
			OrderedInstanceIds = sourceIds,
			ActiveInstanceId = SelectActiveAfterRemoval(source, sourceIds, instanceId)
		};
		string rootId = state.RootNodeId;
		string focused = targetGroupId;
		string? maximised = state.MaximisedGroupId;
		if(sourceIds.Count == 0)
		{
			CollapseEmptyGroup(nodes, source.Id, ref rootId, ref focused, ref maximised);
		}
		if(!nodes.TryGetValue(targetGroupId, out WorkspaceNode? target) || target is not WorkspaceGroupNode)
		{
			return WorkspaceChangeResult.Rejected(state, WorkspaceChangeError.InvalidMove);
		}

		WorkspaceGroupNode newGroup = new(newGroupId, [instanceId], instanceId);
		WorkspaceSplitNode split = placeNewGroupFirst
			? new WorkspaceSplitNode(splitNodeId, axis, ratio, newGroupId, targetGroupId)
			: new WorkspaceSplitNode(splitNodeId, axis, ratio, targetGroupId, newGroupId);
		ReplaceNodeReference(nodes, rootId, targetGroupId, splitNodeId, out rootId);
		nodes[newGroupId] = newGroup;
		nodes[splitNodeId] = split;
		return CommitLayout(state, nodes, state.Instances, rootId, newGroupId, maximisedGroupId: null, setMaximised: true);
	}

	internal static WorkspaceChangeResult SplitRootWithExistingInstance(
		WorkspaceState state,
		string instanceId,
		string newGroupId,
		string splitNodeId,
		WorkspaceSplitAxis axis,
		bool placeNewGroupFirst,
		double ratio = 0.5,
		long? expectedLayoutRevision = null)
	{
		if(IsStale(state, expectedLayoutRevision))
		{
			return RejectStale(state);
		}
		if(!WorkspaceLayoutPolicy.CanSplitRootWithExistingInstance(state, instanceId))
		{
			return WorkspaceChangeResult.Rejected(state, WorkspaceChangeError.SectionLimitReached, $"A workspace can contain at most {WorkspaceLayoutPolicy.MaxSections} sections.");
		}
		if(!TryFindInstanceGroup(state, instanceId, out WorkspaceGroupNode? source))
		{
			return WorkspaceChangeResult.Rejected(state, WorkspaceChangeError.InstanceNotFound);
		}
		if(source.OrderedInstanceIds.Count == 1 && state.Nodes.Values.Count(node => node is WorkspaceGroupNode) == 1)
		{
			return WorkspaceChangeResult.Rejected(state, WorkspaceChangeError.InvalidMove, "Moving the only tab cannot produce two useful groups.");
		}
		if(state.Nodes.ContainsKey(newGroupId) || state.Nodes.ContainsKey(splitNodeId) || newGroupId == splitNodeId)
		{
			return WorkspaceChangeResult.Rejected(state, WorkspaceChangeError.DuplicateId);
		}
		if(!double.IsFinite(ratio) || ratio <= 0 || ratio >= 1)
		{
			return WorkspaceChangeResult.Rejected(state, WorkspaceChangeError.InvalidRatio);
		}

		Dictionary<string, WorkspaceNode> nodes = CopyNodes(state);
		List<string> sourceIds = [.. source.OrderedInstanceIds];
		sourceIds.Remove(instanceId);
		nodes[source.Id] = source with
		{
			OrderedInstanceIds = sourceIds,
			ActiveInstanceId = SelectActiveAfterRemoval(source, sourceIds, instanceId)
		};
		string rootId = state.RootNodeId;
		string focused = source.Id;
		string? maximised = state.MaximisedGroupId;
		if(sourceIds.Count == 0)
		{
			CollapseEmptyGroup(nodes, source.Id, ref rootId, ref focused, ref maximised);
		}

		WorkspaceGroupNode newGroup = new(newGroupId, [instanceId], instanceId);
		WorkspaceSplitNode split = placeNewGroupFirst
			? new WorkspaceSplitNode(splitNodeId, axis, ratio, newGroupId, rootId)
			: new WorkspaceSplitNode(splitNodeId, axis, ratio, rootId, newGroupId);
		nodes[newGroupId] = newGroup;
		nodes[splitNodeId] = split;
		return CommitLayout(state, nodes, state.Instances, splitNodeId, newGroupId, maximisedGroupId: null, setMaximised: true);
	}

	internal static WorkspaceChangeResult SetSplitRatio(WorkspaceState state, string splitNodeId, double ratio, long? expectedLayoutRevision = null)
	{
		if(IsStale(state, expectedLayoutRevision))
		{
			return RejectStale(state);
		}
		if(!state.Nodes.TryGetValue(splitNodeId, out WorkspaceNode? node) || node is not WorkspaceSplitNode split)
		{
			return WorkspaceChangeResult.Rejected(state, WorkspaceChangeError.NodeNotFound);
		}
		if(!double.IsFinite(ratio) || ratio <= 0 || ratio >= 1)
		{
			return WorkspaceChangeResult.Rejected(state, WorkspaceChangeError.InvalidRatio);
		}
		if(split.Ratio == ratio)
		{
			return WorkspaceChangeResult.NoChange(state);
		}

		Dictionary<string, WorkspaceNode> nodes = CopyNodes(state);
		nodes[splitNodeId] = split with { Ratio = ratio };
		return CommitLayout(state, nodes, state.Instances);
	}

	internal static WorkspaceChangeResult FocusGroup(WorkspaceState state, string groupId)
	{
		if(!TryGetGroup(state, groupId, out _))
		{
			return WorkspaceChangeResult.Rejected(state, WorkspaceChangeError.GroupNotFound);
		}
		return state.FocusedGroupId == groupId
			? WorkspaceChangeResult.NoChange(state)
			: CommitLayout(state, state.Nodes, state.Instances, focusedGroupId: groupId);
	}

	internal static WorkspaceChangeResult MaximiseGroup(WorkspaceState state, string groupId)
	{
		if(!TryGetGroup(state, groupId, out _))
		{
			return WorkspaceChangeResult.Rejected(state, WorkspaceChangeError.GroupNotFound);
		}
		return state.MaximisedGroupId == groupId
			? WorkspaceChangeResult.NoChange(state)
			: CommitLayout(state, state.Nodes, state.Instances, maximisedGroupId: groupId);
	}

	internal static WorkspaceChangeResult RestoreGroups(WorkspaceState state)
		=> state.MaximisedGroupId is null
			? WorkspaceChangeResult.NoChange(state)
			: CommitLayout(state, state.Nodes, state.Instances, maximisedGroupId: null, setMaximised: true);

	internal static WorkspaceChangeResult ReplaceWorkspace(WorkspaceState current, WorkspaceState replacement)
	{
		WorkspaceValidationResult validation = WorkspaceValidation.Validate(replacement);
		if(!validation.IsValid)
		{
			return WorkspaceChangeResult.Rejected(current, WorkspaceChangeError.InvalidWorkspace, validation.Error);
		}
		WorkspaceState committed = replacement with
		{
			RuntimeSessionKey = current.RuntimeSessionKey,
			SessionStorageId = current.SessionStorageId,
			IdentityEpoch = current.IdentityEpoch,
			Revision = current.Revision + 1,
			LayoutRevision = current.LayoutRevision + 1,
			PersistedRevision = current.PersistedRevision
		};
		return WorkspaceChangeResult.Applied(committed);
	}

	internal static WorkspaceState CommitStateOnly(WorkspaceState state)
		=> state with { Revision = state.Revision + 1 };

	internal static WorkspaceState SetToolAvailability(
		WorkspaceState state,
		string toolId,
		WorkspaceAvailabilityState availability)
	{
		if(state.Availability.TryGetValue(toolId, out WorkspaceAvailabilityState? current) && current == availability)
		{
			return state;
		}
		Dictionary<string, WorkspaceAvailabilityState> values = new(state.Availability, StringComparer.Ordinal)
		{
			[toolId] = availability
		};
		// Availability is session-runtime projection data and is deliberately absent from the
		// durable manifest, so refreshing it must not mark workspace content dirty.
		return state with { Availability = values };
	}

	internal static WorkspaceState UpdateCheckpoint(
		WorkspaceState state,
		string instanceId,
		WorkspaceCheckpointReference checkpoint)
	{
		if(!state.Instances.TryGetValue(instanceId, out WorkspaceInstance? instance))
		{
			throw new ArgumentException("The workspace instance does not exist.", nameof(instanceId));
		}
		if(instance.Checkpoint is not null && checkpoint.StateVersion <= instance.Checkpoint.StateVersion)
		{
			return state;
		}
		Dictionary<string, WorkspaceInstance> instances = CopyInstances(state);
		instances[instanceId] = instance with { Checkpoint = checkpoint };
		return state with { Revision = state.Revision + 1, Instances = instances };
	}

	static WorkspaceChangeResult CommitLayout(
		WorkspaceState state,
		IReadOnlyDictionary<string, WorkspaceNode> nodes,
		IReadOnlyDictionary<string, WorkspaceInstance> instances,
		string? rootNodeId = null,
		string? focusedGroupId = null,
		string? maximisedGroupId = null,
		bool setMaximised = false)
	{
		WorkspaceState next = state with
		{
			Revision = state.Revision + 1,
			LayoutRevision = state.LayoutRevision + 1,
			RootNodeId = rootNodeId ?? state.RootNodeId,
			FocusedGroupId = focusedGroupId ?? state.FocusedGroupId,
			MaximisedGroupId = setMaximised ? maximisedGroupId : maximisedGroupId ?? state.MaximisedGroupId,
			Nodes = nodes,
			Instances = instances
		};
		WorkspaceValidationResult validation = WorkspaceValidation.Validate(next);
		return validation.IsValid
			? WorkspaceChangeResult.Applied(next)
			: WorkspaceChangeResult.Rejected(state, WorkspaceChangeError.InvalidWorkspace, validation.Error);
	}

	static bool TryGetGroup(WorkspaceState state, string id, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out WorkspaceGroupNode? group)
	{
		group = state.Nodes.TryGetValue(id, out WorkspaceNode? node) ? node as WorkspaceGroupNode : null;
		return group is not null;
	}

	static bool TryFindInstanceGroup(WorkspaceState state, string instanceId, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out WorkspaceGroupNode? group)
	{
		group = state.Nodes.Values.OfType<WorkspaceGroupNode>()
			.FirstOrDefault(candidate => candidate.OrderedInstanceIds.Contains(instanceId, StringComparer.Ordinal));
		return group is not null;
	}

	static string? SelectActiveAfterRemoval(WorkspaceGroupNode group, List<string> remaining, string removed)
	{
		if(group.ActiveInstanceId != removed)
		{
			return group.ActiveInstanceId;
		}
		int oldIndex = IndexOf(group.OrderedInstanceIds, removed);
		return remaining.ElementAtOrDefault(Math.Min(oldIndex, remaining.Count - 1));
	}

	static int IndexOf(IReadOnlyList<string> values, string value)
	{
		for(int index = 0; index < values.Count; index++)
		{
			if(string.Equals(values[index], value, StringComparison.Ordinal))
			{
				return index;
			}
		}
		return -1;
	}

	static void CollapseEmptyGroup(
		Dictionary<string, WorkspaceNode> nodes,
		string emptyGroupId,
		ref string rootId,
		ref string focusedGroupId,
		ref string? maximisedGroupId)
	{
		if(rootId == emptyGroupId)
		{
			return;
		}
		WorkspaceSplitNode? parent = nodes.Values.OfType<WorkspaceSplitNode>()
			.FirstOrDefault(split => split.FirstNodeId == emptyGroupId || split.SecondNodeId == emptyGroupId);
		if(parent is null)
		{
			return;
		}
		string siblingId = parent.FirstNodeId == emptyGroupId ? parent.SecondNodeId : parent.FirstNodeId;
		ReplaceNodeReference(nodes, rootId, parent.Id, siblingId, out rootId);
		nodes.Remove(emptyGroupId);
		nodes.Remove(parent.Id);
		if(focusedGroupId == emptyGroupId)
		{
			focusedGroupId = FirstGroupId(nodes, siblingId);
		}
		if(maximisedGroupId == emptyGroupId)
		{
			maximisedGroupId = null;
		}
	}

	static void ReplaceNodeReference(
		Dictionary<string, WorkspaceNode> nodes,
		string currentRootId,
		string oldNodeId,
		string replacementNodeId,
		out string newRootId)
	{
		if(currentRootId == oldNodeId)
		{
			newRootId = replacementNodeId;
			return;
		}

		WorkspaceSplitNode parent = nodes.Values.OfType<WorkspaceSplitNode>()
			.FirstOrDefault(split => split.FirstNodeId == oldNodeId || split.SecondNodeId == oldNodeId)
			?? throw new InvalidOperationException($"Node '{oldNodeId}' is not attached to the root.");

		nodes[parent.Id] = parent.FirstNodeId == oldNodeId
			? parent with { FirstNodeId = replacementNodeId }
			: parent with { SecondNodeId = replacementNodeId };
		newRootId = currentRootId;
	}

	static string FirstGroupId(Dictionary<string, WorkspaceNode> nodes, string rootId)
	{
		string current = rootId;
		while(nodes[current] is WorkspaceSplitNode split)
		{
			current = split.FirstNodeId;
		}
		return current;
	}

	static Dictionary<string, WorkspaceNode> CopyNodes(WorkspaceState state)
		=> new(state.Nodes, StringComparer.Ordinal);

	static Dictionary<string, WorkspaceInstance> CopyInstances(WorkspaceState state)
		=> new(state.Instances, StringComparer.Ordinal);

	static bool IsStale(WorkspaceState state, long? expected) => expected.HasValue && expected.Value != state.LayoutRevision;
	static WorkspaceChangeResult RejectStale(WorkspaceState state)
		=> WorkspaceChangeResult.Rejected(state, WorkspaceChangeError.StaleCommand);
}
