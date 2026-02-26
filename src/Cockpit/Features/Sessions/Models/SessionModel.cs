using System.Collections.Concurrent;
using Cockpit.Features.Permissions.Models;
using Cockpit.Features.SessionEvents.Models;
using GitHub.Copilot.SDK;
using Cockpit.Features.UserInputRequests;

namespace Cockpit.Features.Sessions.Models;

public class SessionModel
{
	public required string Id { get; set; }
	public required string Title { get; set; }
	public required DateTime CreatedAt { get; set; }
	public required DateTime LastActivity { get; set; }
	public SessionStatusEnum Status { get; set; } = SessionStatusEnum.Active;
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
	public readonly Lock PermissionRequestsLock = new();

	/// <summary>
	/// Pending user input requests for this session (supports multiple concurrent requests)
	/// Key: request.Id, Value: UserInputRequestModel
	/// </summary>
	public ConcurrentDictionary<string, UserInputRequestModel> PendingUserInputRequests { get; set; } = new();
	public readonly Lock UserInputRequestsLock = new();

	/// <summary>
	/// History of statuses before blocking requests (permission/user-input).
	/// Pushed when the first blocking request of a type arrives; popped when all of that type resolve.
	/// </summary>
	public Stack<SessionStatusEnum> StatusHistory { get; } = new();
	public readonly Lock StatusHistoryLock = new();

	/// <summary>
	/// Tracks the SDK connection lifecycle of this session.
	/// </summary>
	public SdkSessionStateEnum SdkState { get; set; } = SdkSessionStateEnum.NotLoaded;
	public bool ModelChanged { get; set; }
	public bool IsYolo { get; set; }
	public bool IsTerminalOpen { get; set; }

	/// <summary>
	/// Per-session draft text preserved across session switches.
	/// </summary>
	public string UserInput { get; set; } = string.Empty;

	/// <summary>
	/// Per-session pending attachments preserved across session switches.
	/// </summary>
	public List<AttachmentModel> PendingAttachments { get; set; } = [];

	/// <summary>
	/// Synchronizes mutations to <see cref="PendingAttachments"/> across threads (e.g. JS-interop paste callbacks vs. UI-thread picks/sends).
	/// </summary>
	public readonly Lock PendingAttachmentsLock = new();

	/// <summary>
	/// Per-session user input response text preserved across session switches.
	/// </summary>
	public string UserInputResponseText { get; set; } = string.Empty;

	/// <summary>
	/// Per-session selected choice for user input request preserved across session switches.
	/// </summary>
	public string? UserInputSelectedChoice { get; set; } = null;

	/// <summary>
	/// Synchronizes live session event/message mutations to preserve ordering.
	/// </summary>
	public readonly Lock SessionEventLock = new();
}