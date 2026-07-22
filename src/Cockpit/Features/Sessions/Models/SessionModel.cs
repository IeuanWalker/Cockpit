using Cockpit.Features.Sessions.Interactions;
using GitHub.Copilot;

namespace Cockpit.Features.Sessions.Models;

public partial class SessionModel
{
	public required string Id { get; set; }
	public required string Title { get; set; }
	public required DateTime CreatedAt { get; set; }
	public required DateTime LastActivity { get; set; }
	/// <summary>
	/// The agent's lifecycle state. Pending interactions must not overwrite this value.
	/// </summary>
	public AgentRunStateEnum AgentRunState { get; set; } = AgentRunStateEnum.Idle;

	/// <summary>
	/// UI-facing status. The interaction coordinator controls the pending-interaction
	/// overlay while <see cref="AgentRunState"/> continues to track the agent lifecycle.
	/// </summary>
	public SessionStatusEnum DisplayStatus => PendingInteractions.DisplayStatus ?? AgentRunState switch
	{
		AgentRunStateEnum.Running => SessionStatusEnum.Running,
		AgentRunStateEnum.Error => SessionStatusEnum.Error,
		_ => SessionStatusEnum.Idle
	};

	/// <summary>
	/// Compatibility alias for consumers that only read session status.
	/// New code should use <see cref="AgentRunState"/> for lifecycle decisions and
	/// <see cref="DisplayStatus"/> for presentation.
	/// </summary>
	public SessionStatusEnum Status => DisplayStatus;

	public PendingInteractionState PendingInteractions { get; } = new();
	public SessionConversationState Conversation { get; } = new();
	public required SessionContext Context { get; set; }
	public required ModelInfo Model { get; set; }
	public string? ReasoningEffort { get; set; }

	/// <summary>
	/// When set, identifies the <see cref="ByokModelConfig"/> that provides the active model.
	/// Null for built-in Copilot models.
	/// </summary>
	public string? ByokConfigId { get; set; }
	/// <summary>
	/// Tracks the SDK connection lifecycle of this session.
	/// </summary>
	public SdkSessionStateEnum SdkState { get; set; } = SdkSessionStateEnum.NotLoaded;
	public bool ModelChanged { get; set; }
	public bool AgentChanged { get; set; }
	public bool AgentModeChanged { get; set; }

	/// <summary>
	/// When <see langword="true"/> the <c>session.idle</c> handler will not raise
	/// <see cref="SessionEvents.Handlers.SessionIdleHandler.OnSessionFinished"/>.
	/// Set during session-history replay to avoid spurious completion notifications.
	/// </summary>
	public bool SuppressFinishedNotification { get; set; }

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

}
