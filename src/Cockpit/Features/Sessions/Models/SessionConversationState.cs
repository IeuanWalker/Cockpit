using System.Collections.Immutable;
using Cockpit.Features.SessionEvents.Models;

namespace Cockpit.Features.Sessions.Models;

/// <summary>
/// Mutable state produced while processing a session's conversation events.
/// All event-driven mutations are serialized through <see cref="SyncRoot"/>.
/// </summary>
public sealed class SessionConversationState
{
	List<ChatMessageModel> _messages = [];

	public List<ChatMessageModel> Messages => _messages;

	/// <summary>
	/// Stable render view published after an event batch has finished mutating
	/// <see cref="Messages"/>.
	/// </summary>
	public ImmutableArray<ChatMessageModel> MessagesSnapshot { get; private set; } = [];

	/// <summary>
	/// Publishes the current mutable message collection as an immutable render snapshot.
	/// Existing callers may already hold <see cref="SyncRoot"/>; the lock is re-entrant.
	/// </summary>
	internal void PublishMessagesSnapshot()
	{
		lock(SyncRoot)
		{
			MessagesSnapshot = [.. _messages];
		}
	}

	/// <summary>
	/// Replaces conversation history without retaining the caller's mutable collection.
	/// </summary>
	internal void ReplaceMessages(IEnumerable<ChatMessageModel> messages)
	{
		lock(SyncRoot)
		{
			_messages = [.. messages];
			MessagesSnapshot = [.. _messages];
		}
	}

	/// <summary>
	/// Clears mutable history and publishes an empty snapshot as one transition.
	/// </summary>
	internal void ClearMessages()
	{
		lock(SyncRoot)
		{
			_messages.Clear();
			MessagesSnapshot = [];
		}
	}

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
