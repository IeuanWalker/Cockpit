namespace Cockpit.Models;

/// <summary>
/// Represents a pending permission request from an agent
/// </summary>
public class PermissionRequest
{
	/// <summary>
	/// Unique identifier for this request
	/// </summary>
	public string Id { get; set; } = Guid.NewGuid().ToString();

	/// <summary>
	/// ID of the session that requested this permission
	/// </summary>
	public required string SessionId { get; set; }

	/// <summary>
	/// Name of the tool being requested (e.g., "bash", "edit", "read")
	/// </summary>
	public required string ToolName { get; set; }

	/// <summary>
	/// Kind of permission request (e.g., "write", "read", "shell", "url")
	/// </summary>
	public string? Kind { get; set; }

	/// <summary>
	/// Command or action being requested (e.g., "npm test")
	/// </summary>
	public required string Command { get; set; }

	/// <summary>
	/// Arguments/parameters for the tool execution
	/// </summary>
	public Dictionary<string, object>? Arguments { get; set; }

	/// <summary>
	/// When this request was created
	/// </summary>
	public DateTime Timestamp { get; set; } = DateTime.UtcNow;

	/// <summary>
	/// TaskCompletionSource to await user decision
	/// </summary>
	public TaskCompletionSource<PermissionDecision> CompletionSource { get; } = new();

	/// <summary>
	/// Get the awaitable task for user decision
	/// </summary>
	public Task<PermissionDecision> GetDecisionAsync() => CompletionSource.Task;
}

/// <summary>
/// User's decision on a permission request
/// </summary>
public class PermissionDecision
{
	/// <summary>
	/// Whether the user approved or denied the request
	/// </summary>
	public bool IsApproved { get; set; }

	/// <summary>
	/// Scope of the approval (if approved)
	/// </summary>
	public PermissionScope Scope { get; set; } = PermissionScope.Once;

	/// <summary>
	/// Optional reason for denial
	/// </summary>
	public string? Reason { get; set; }
}
