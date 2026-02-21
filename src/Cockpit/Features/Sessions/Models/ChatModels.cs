using System.Collections.Concurrent;
using Cockpit.Features.Permissions.Models;
using Cockpit.Features.SessionEvents.Models;
using GitHub.Copilot.SDK;

namespace Cockpit.Features.Sessions.Models;

public class ChatSession
{
	public required string Id { get; set; }
	public required string Title { get; set; }
	public required DateTime CreatedAt { get; set; }
	public required DateTime LastActivity { get; set; }
	public SessionStatus Status { get; set; } = SessionStatus.Active;
	public List<ChatMessageModel> Messages { get; set; } = [];
	public required SessionContext Context { get; set; }
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

	/// <summary>
	/// Synchronizes live session event/message mutations to preserve ordering.
	/// </summary>
	public readonly Lock SessionEventLock = new();
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