namespace Cockpit.Features.Workspace;

enum WorkspaceSplitAxis
{
	Columns,
	Rows
}

enum WorkspaceNodeKind
{
	Group,
	Split
}

enum WorkspaceToolAvailability
{
	Available,
	Unavailable,
	EvaluationFailed,
	Missing
}

abstract record WorkspaceNode(string Id)
{
	internal abstract WorkspaceNodeKind Kind { get; }
}

sealed record WorkspaceGroupNode(
	string Id,
	IReadOnlyList<string> OrderedInstanceIds,
	string? ActiveInstanceId = null) : WorkspaceNode(Id)
{
	internal override WorkspaceNodeKind Kind => WorkspaceNodeKind.Group;
}

sealed record WorkspaceSplitNode(
	string Id,
	WorkspaceSplitAxis Axis,
	double Ratio,
	string FirstNodeId,
	string SecondNodeId) : WorkspaceNode(Id)
{
	internal override WorkspaceNodeKind Kind => WorkspaceNodeKind.Split;
}

sealed record WorkspaceCheckpointReference(
	string ContentId,
	int SchemaVersion,
	long StateVersion,
	IReadOnlyList<string>? AssetContentIds = null);

sealed record WorkspaceInstance(
	string Id,
	string ToolId,
	string? TitleOverride = null,
	WorkspaceCheckpointReference? Checkpoint = null);

sealed record WorkspaceAvailabilityState(
	WorkspaceToolAvailability Status,
	string? Reason = null)
{
	internal static readonly WorkspaceAvailabilityState available = new(WorkspaceToolAvailability.Available);
}

sealed record WorkspaceState
{
	internal required string RuntimeSessionKey { get; init; }
	internal required string SessionStorageId { get; init; }
	internal long IdentityEpoch { get; init; }
	internal long Revision { get; init; }
	internal long LayoutRevision { get; init; }
	internal long PersistedRevision { get; init; }
	internal required string RootNodeId { get; init; }
	internal required string FocusedGroupId { get; init; }
	internal string? MaximisedGroupId { get; init; }
	internal required IReadOnlyDictionary<string, WorkspaceNode> Nodes { get; init; }
	internal required IReadOnlyDictionary<string, WorkspaceInstance> Instances { get; init; }
	internal IReadOnlyDictionary<string, WorkspaceAvailabilityState> Availability { get; init; }
		= new Dictionary<string, WorkspaceAvailabilityState>(StringComparer.Ordinal);

	internal static WorkspaceState Empty(string runtimeSessionKey, string sessionStorageId, string rootGroupId)
	{
		WorkspaceGroupNode root = new(rootGroupId, []);
		return new WorkspaceState
		{
			RuntimeSessionKey = runtimeSessionKey,
			SessionStorageId = sessionStorageId,
			RootNodeId = rootGroupId,
			FocusedGroupId = rootGroupId,
			Nodes = new Dictionary<string, WorkspaceNode>(StringComparer.Ordinal) { [rootGroupId] = root },
			Instances = new Dictionary<string, WorkspaceInstance>(StringComparer.Ordinal)
		};
	}
}

enum WorkspaceChangeStatus
{
	Applied,
	NoChange,
	Rejected
}

enum WorkspaceChangeError
{
	None,
	InvalidWorkspace,
	NodeNotFound,
	GroupNotFound,
	InstanceNotFound,
	DuplicateId,
	InvalidIndex,
	InvalidRatio,
	InvalidMove,
	SectionLimitReached,
	StaleCommand
}

sealed record WorkspaceChangeResult(
	WorkspaceChangeStatus Status,
	WorkspaceState State,
	WorkspaceChangeError Error = WorkspaceChangeError.None,
	string? Detail = null)
{
	internal static WorkspaceChangeResult Applied(WorkspaceState state) => new(WorkspaceChangeStatus.Applied, state);
	internal static WorkspaceChangeResult NoChange(WorkspaceState state) => new(WorkspaceChangeStatus.NoChange, state);
	internal static WorkspaceChangeResult Rejected(WorkspaceState state, WorkspaceChangeError error, string? detail = null)
		=> new(WorkspaceChangeStatus.Rejected, state, error, detail);
}
