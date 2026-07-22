using Cockpit.Features.SessionEvents.Models;

namespace Cockpit.Features.Sessions.Models;

/// <summary>
/// Mutable state produced while processing a session's conversation events.
/// All event-driven mutations are serialized through <see cref="SyncRoot"/>.
/// </summary>
public sealed class SessionConversationState
{
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
	/// Stable render view published after an event batch has finished mutating
	/// <see cref="Messages"/>.
	/// </summary>
	public IReadOnlyList<ChatMessageModel> MessagesSnapshot { get; internal set; } = [];

	public ActivityGroupModel? ActiveWorkingGroup { get; set; }
	public Dictionary<string, ChatMessageModel> StreamingMessages { get; } = [];
	public Dictionary<string, ThinkingEventModel> StreamingThinkingEvents { get; } = [];

	public int PendingMessageCount { get; set; }
	public TokenUsageInfoModel? TokenUsageInfo { get; set; }
	public bool IsCompacting { get; set; }
	public bool AgentTurnCompleted { get; set; }
	public bool HasQueuedImmediateMessage { get; set; }
	public string? PendingTaskSummary { get; set; }

	public Lock SyncRoot { get; } = new();
}
