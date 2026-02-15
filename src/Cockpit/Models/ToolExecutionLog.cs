using Cockpit.Features.Permissions.Models;

namespace Cockpit.Models;

/// <summary>
/// Audit log entry for tool execution
/// </summary>
public class ToolExecutionLog
{
	/// <summary>
	/// Unique identifier for this log entry
	/// </summary>
	public string Id { get; set; } = Guid.NewGuid().ToString();

	/// <summary>
	/// ID of the session that executed the tool
	/// </summary>
	public required string SessionId { get; set; }

	/// <summary>
	/// Name of the tool that was executed
	/// </summary>
	public required string ToolName { get; set; }

	/// <summary>
	/// Command or action that was executed
	/// </summary>
	public required string Command { get; set; }

	/// <summary>
	/// Arguments/parameters used in execution
	/// </summary>
	public Dictionary<string, object>? Arguments { get; set; }

	/// <summary>
	/// Whether the tool was approved and executed
	/// </summary>
	public bool WasApproved { get; set; }

	/// <summary>
	/// How the permission was granted (if approved)
	/// </summary>
	public PermissionScope? ApprovalScope { get; set; }

	/// <summary>
	/// When this execution occurred
	/// </summary>
	public DateTime Timestamp { get; set; } = DateTime.UtcNow;

	/// <summary>
	/// Whether the execution succeeded
	/// </summary>
	public bool? Success { get; set; }

	/// <summary>
	/// Error message if execution failed
	/// </summary>
	public string? ErrorMessage { get; set; }
}
