using System.Collections.Concurrent;
using Cockpit.Features.Permissions.Models;
using Cockpit.Features.SessionEvents.Models;
using GitHub.Copilot.SDK;
using SessionContextModel = Cockpit.Models.SessionContext;

namespace Cockpit.Models;

public class ChatSession
{
	public string Id { get; set; } = Guid.NewGuid().ToString();
	public string Title { get; set; } = "New Session";
	public DateTime CreatedAt { get; set; } = DateTime.Now;
	public DateTime LastActivity { get; set; } = DateTime.Now;
	public SessionStatus Status { get; set; } = SessionStatus.Active;
	public List<ChatMessageModel> Messages { get; set; } = [];
	public string? WorkspacePath { get; set; }
	public string? WorkingDirectory { get; set; }
	public SessionContextModel Context { get; set; } = SessionContextModel.CreateDefault();
	public required ModelInfo Model { get; set; }
	public string? ReasoningEffort { get; set; }
	public ActivityGroupModel? ActiveWorkingGroup { get; set; }
	public Dictionary<string, ChatMessageModel> StreamingMessages { get; } = [];

	/// <summary>
	/// Pending permission requests for this session (supports multiple concurrent requests)
	/// Key: request.Id, Value: PermissionRequest
	/// </summary>
	public ConcurrentDictionary<string, PermissionRequestModel> PendingPermissionRequests { get; set; } = new();

	/// <summary>
	/// Lock for coordinating permission request status changes
	/// </summary>
	public readonly Lock PermissionRequestsLock = new();

	/// <summary>
	/// Previous status before permission request (to restore after all decisions)
	/// </summary>
	public SessionStatus? PreviousStatus { get; set; }

	/// <summary>
	/// Whether this session has an active SDK connection
	/// </summary>
	public bool IsResumed { get; set; }

	/// <summary>
	/// Whether this session is currently loading/replaying history (shows loading indicator in UI)
	/// </summary>
	public bool IsResuming { get; set; }

	/// <summary>
	/// Whether this session requires a restart to apply configuration changes (model/reasoning effort)
	/// </summary>
	public bool RequiresRestart { get; set; }

	/// <summary>
	/// YOLO mode - automatically accept all permissions without prompting
	/// </summary>
	public bool IsYolo { get; set; }

	/// <summary>
	/// Terminal state for this session
	/// </summary>
	public bool IsTerminalOpen { get; set; }
}

public enum SessionStatus
{
	Idle,
	Active,
	Running,
	NeedsPermission,
	Finished,
	Error,
	Archived
}





// Thinking event - can be either a message or a tool execution




// Activity group for multiple tool executions


// Individual tool execution





public class ContextFile
{
	public string Name { get; set; } = string.Empty;
	public string Path { get; set; } = string.Empty;
	public FileStatus Status { get; set; } = FileStatus.Unmodified;
}

public enum FileStatus
{
	Unmodified,
	Modified,
	Added,
	Deleted
}
