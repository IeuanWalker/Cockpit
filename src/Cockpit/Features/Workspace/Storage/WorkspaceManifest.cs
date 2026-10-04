namespace Cockpit.Features.Workspace.Storage;

sealed class WorkspaceManifest
{
	public int SchemaVersion { get; init; } = currentSchemaVersion;
	public required string SessionStorageId { get; init; }
	public string? SdkSessionId { get; init; }
	public long IdentityEpoch { get; init; }
	public long Revision { get; init; }
	public long LayoutRevision { get; init; }
	public required string RootNodeId { get; init; }
	public required string FocusedGroupId { get; init; }
	public string? MaximisedGroupId { get; init; }
	public required IReadOnlyList<WorkspaceNodeManifest> Nodes { get; init; }
	public required IReadOnlyList<WorkspaceInstanceManifest> Instances { get; init; }

	internal const int currentSchemaVersion = 1;

	internal static WorkspaceManifest FromState(WorkspaceState state, string? sdkSessionId = null)
	{
		List<WorkspaceNodeManifest> nodes = new(state.Nodes.Count);
		foreach(WorkspaceNode node in state.Nodes.Values.OrderBy(node => node.Id, StringComparer.Ordinal))
		{
			nodes.Add(node switch
			{
				WorkspaceGroupNode group => new WorkspaceNodeManifest
				{
					Id = group.Id,
					Kind = WorkspaceNodeKind.Group,
					OrderedInstanceIds = group.OrderedInstanceIds,
					ActiveInstanceId = group.ActiveInstanceId
				},
				WorkspaceSplitNode split => new WorkspaceNodeManifest
				{
					Id = split.Id,
					Kind = WorkspaceNodeKind.Split,
					Axis = split.Axis,
					Ratio = split.Ratio,
					FirstNodeId = split.FirstNodeId,
					SecondNodeId = split.SecondNodeId
				},
				_ => throw new InvalidOperationException($"Unsupported workspace node '{node.GetType().Name}'.")
			});
		}

		return new WorkspaceManifest
		{
			SessionStorageId = state.SessionStorageId,
			SdkSessionId = sdkSessionId,
			IdentityEpoch = state.IdentityEpoch,
			Revision = state.Revision,
			LayoutRevision = state.LayoutRevision,
			RootNodeId = state.RootNodeId,
			FocusedGroupId = state.FocusedGroupId,
			MaximisedGroupId = state.MaximisedGroupId,
			Nodes = nodes,
			Instances = state.Instances.Values
				.OrderBy(instance => instance.Id, StringComparer.Ordinal)
				.Select(instance => new WorkspaceInstanceManifest
				{
					Id = instance.Id,
					ToolId = instance.ToolId,
					TitleOverride = instance.TitleOverride,
					Checkpoint = instance.Checkpoint is null ? null : new WorkspaceCheckpointManifest
					{
						ContentId = instance.Checkpoint.ContentId,
						SchemaVersion = instance.Checkpoint.SchemaVersion,
						StateVersion = instance.Checkpoint.StateVersion,
						AssetContentIds = instance.Checkpoint.AssetContentIds ?? []
					}
				})
				.ToArray()
		};
	}

	internal WorkspaceState ToState(string runtimeSessionKey)
	{
		if(string.IsNullOrWhiteSpace(runtimeSessionKey)
			|| string.IsNullOrWhiteSpace(SessionStorageId)
			|| string.IsNullOrWhiteSpace(RootNodeId)
			|| string.IsNullOrWhiteSpace(FocusedGroupId)
			|| Nodes is null
			|| Instances is null)
		{
			throw new InvalidDataException("The workspace manifest is missing required identity or layout data.");
		}
		Dictionary<string, WorkspaceNode> nodes = new(StringComparer.Ordinal);
		foreach(WorkspaceNodeManifest node in Nodes)
		{
			if(string.IsNullOrWhiteSpace(node.Id))
			{
				throw new InvalidDataException("A workspace node has no ID.");
			}
			WorkspaceNode converted = node.Kind switch
			{
				WorkspaceNodeKind.Group => new WorkspaceGroupNode(node.Id, node.OrderedInstanceIds ?? [], node.ActiveInstanceId),
				WorkspaceNodeKind.Split when node.Axis.HasValue && node.FirstNodeId is not null && node.SecondNodeId is not null
					=> new WorkspaceSplitNode(node.Id, node.Axis.Value, node.Ratio, node.FirstNodeId, node.SecondNodeId),
				_ => throw new InvalidDataException($"Node '{node.Id}' is incomplete.")
			};
			if(!nodes.TryAdd(converted.Id, converted))
			{
				throw new InvalidDataException($"Node '{converted.Id}' is duplicated.");
			}
		}

		Dictionary<string, WorkspaceInstance> instances = new(StringComparer.Ordinal);
		foreach(WorkspaceInstanceManifest instance in Instances)
		{
			if(string.IsNullOrWhiteSpace(instance.Id) || string.IsNullOrWhiteSpace(instance.ToolId))
			{
				throw new InvalidDataException("A workspace instance has no instance ID or tool ID.");
			}
			WorkspaceCheckpointReference? checkpoint = instance.Checkpoint is null
				? null
				: new WorkspaceCheckpointReference(
					instance.Checkpoint.ContentId,
					instance.Checkpoint.SchemaVersion,
					instance.Checkpoint.StateVersion,
					instance.Checkpoint.AssetContentIds);
			if(!instances.TryAdd(instance.Id, new WorkspaceInstance(instance.Id, instance.ToolId, instance.TitleOverride, checkpoint)))
			{
				throw new InvalidDataException($"Instance '{instance.Id}' is duplicated.");
			}
		}

		WorkspaceState state = new()
		{
			RuntimeSessionKey = runtimeSessionKey,
			SessionStorageId = SessionStorageId,
			IdentityEpoch = IdentityEpoch,
			Revision = Revision,
			LayoutRevision = LayoutRevision,
			PersistedRevision = Revision,
			RootNodeId = RootNodeId,
			FocusedGroupId = FocusedGroupId,
			MaximisedGroupId = MaximisedGroupId,
			Nodes = nodes,
			Instances = instances
		};
		WorkspaceValidationResult validation = WorkspaceValidation.Validate(state);
		if(!validation.IsValid)
		{
			throw new InvalidDataException(validation.Error);
		}
		return state;
	}
}

sealed class WorkspaceNodeManifest
{
	public required string Id { get; init; }
	public WorkspaceNodeKind Kind { get; init; }
	public IReadOnlyList<string>? OrderedInstanceIds { get; init; }
	public string? ActiveInstanceId { get; init; }
	public WorkspaceSplitAxis? Axis { get; init; }
	public double Ratio { get; init; }
	public string? FirstNodeId { get; init; }
	public string? SecondNodeId { get; init; }
}

sealed class WorkspaceInstanceManifest
{
	public required string Id { get; init; }
	public required string ToolId { get; init; }
	public string? TitleOverride { get; init; }
	public WorkspaceCheckpointManifest? Checkpoint { get; init; }
}

sealed class WorkspaceCheckpointManifest
{
	public required string ContentId { get; init; }
	public int SchemaVersion { get; init; }
	public long StateVersion { get; init; }
	public IReadOnlyList<string> AssetContentIds { get; init; } = [];
}
