namespace Cockpit.Tooling;

public interface IWorkspaceToolHost
{
	ValueTask<WorkspaceHostCommandResult> ActivateSelfAsync(CancellationToken cancellationToken);
	ValueTask<WorkspaceHostCommandResult> CloseSelfAsync(CancellationToken cancellationToken);
	ValueTask<WorkspaceHostCommandResult> OpenNewToolAsync(string toolId, CancellationToken cancellationToken);
	ValueTask<WorkspaceHostCommandResult> SetTitleAsync(string? title, CancellationToken cancellationToken);
	ValueTask<WorkspaceHostCommandResult> RequestLayoutNotificationAsync(CancellationToken cancellationToken);
	ValueTask<WorkspaceHostCommandResult> AppendToComposerAsync(string content, CancellationToken cancellationToken);
	ValueTask<WorkspaceHostCommandResult> RevealFileAsync(string path, CancellationToken cancellationToken);
}

public enum WorkspaceHostCommandStatus
{
	Applied,
	NoChange,
	SessionNotCurrent,
	InstanceClosed,
	InvalidTarget,
	Rejected
}

public sealed record WorkspaceHostCommandResult(WorkspaceHostCommandStatus Status, string? Message = null)
{
	public static WorkspaceHostCommandResult Applied() => new(WorkspaceHostCommandStatus.Applied);
}

public enum ToolCloseReason
{
	UserClosedTab,
	WorkspaceReset,
	SessionDeleted,
	ApplicationShutdown
}

public enum ToolCloseDecisionKind { Allow, Cancel, Confirm }

public sealed record ToolCloseDecision(
	ToolCloseDecisionKind Kind,
	string? Message = null,
	string ConfirmLabel = "Close",
	string CancelLabel = "Cancel")
{
	public static ToolCloseDecision Allow() => new(ToolCloseDecisionKind.Allow);
	public static ToolCloseDecision Cancel() => new(ToolCloseDecisionKind.Cancel);
	public static ToolCloseDecision Confirm(string message, string confirmLabel = "Close") =>
		new(ToolCloseDecisionKind.Confirm, message, confirmLabel);
}
