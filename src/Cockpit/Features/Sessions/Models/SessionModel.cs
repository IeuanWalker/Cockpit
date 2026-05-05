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
	List<ChatMessageModel> _messages = [];
	public List<ChatMessageModel> Messages
	{
		get => _messages;
		set
		{
			_messages = value;
			MessagesSnapshot = [.. value];
		}
	}

	/// <summary>
	/// A snapshot of <see cref="Messages"/> taken inside <see cref="SessionEventLock"/> after each event is processed,
	/// or whenever <see cref="Messages"/> is replaced. The Blazor renderer reads this instead of <see cref="Messages"/>
	/// directly to avoid concurrent-modification exceptions.
	/// </summary>
	public IReadOnlyList<ChatMessageModel> MessagesSnapshot { get; internal set; } = [];
	public required SessionContext Context { get; set; }
	public required ModelInfo Model { get; set; }
	public string? ReasoningEffort { get; set; }
	public ActivityGroupModel? ActiveWorkingGroup { get; set; }
	public Dictionary<string, ChatMessageModel> StreamingMessages { get; } = [];

	/// <summary>
	/// Live-streaming <see cref="ThinkingEventModel"/> instances keyed by message ID.
	/// Created by <c>AssistantMessageDeltaHandler</c> so subsequent deltas update the same
	/// thinking-panel entry rather than creating duplicates, and cleaned up by
	/// <c>AssistantMessageHandler</c> when the complete message arrives.
	/// </summary>
	public Dictionary<string, ThinkingEventModel> StreamingThinkingEvents { get; } = [];

	/// <summary>
	/// Pending permission requests for this session (supports multiple concurrent requests)
	/// Key: request.Id, Value: PermissionRequest
	/// </summary>
	public ConcurrentDictionary<string, PermissionRequestModel> PendingPermissionRequests { get; set; } = new();

	/// <summary>
	/// Pending user input requests for this session (supports multiple concurrent requests)
	/// Key: request.Id, Value: UserInputRequestModel
	/// </summary>
	public ConcurrentDictionary<string, UserInputRequestModel> PendingUserInputRequests { get; set; } = new();

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
	public bool AgentChanged { get; set; }

	/// <summary>
	/// When <see langword="true"/> the <c>session.idle</c> handler will not raise
	/// <see cref="SessionEvents.Handlers.SessionIdleHandler.OnSessionFinished"/>.
	/// Set during session-history replay to avoid spurious completion notifications.
	/// </summary>
	public bool SuppressFinishedNotification { get; set; }

	/// <summary>
	/// Set by <c>SessionTaskCompleteHandler</c> when the SDK emits a <c>session.task_complete</c>
	/// event. Consumed and cleared by <c>SessionIdleHandler</c> as the preferred summary source,
	/// with the heuristic last-message extraction kept as a fallback.
	/// </summary>
	public string? PendingTaskSummary { get; set; }
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
	/// Synchronizes live session event/message mutations to preserve ordering.
	/// </summary>
	public readonly Lock SessionEventLock = new();
}