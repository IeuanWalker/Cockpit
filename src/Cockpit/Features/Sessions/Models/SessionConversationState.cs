using System.Collections.ObjectModel;
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
	ReadOnlyCollection<ChatMessageModel> _messagesView;
	long _structuralVersion;
	long _publishedStructuralVersion;

	public SessionConversationState()
	{
		_messagesView = _messages.AsReadOnly();
	}

	/// <summary>
	/// Live, read-only access to the messages currently being processed. Structural
	/// changes must go through the controlled methods below so version tracking cannot
	/// be bypassed.
	/// </summary>
	public IReadOnlyList<ChatMessageModel> Messages => _messagesView;

	/// <summary>
	/// Stable render view published after an event batch has finished mutating
	/// <see cref="Messages"/>.
	/// </summary>
	public ImmutableArray<ChatMessageModel> MessagesSnapshot { get; private set; } = [];

	internal long StructuralVersion => _structuralVersion;
	internal int RetainedMessageCapacity => _messages.Capacity;

	internal int IndexOfMessage(ChatMessageModel message) => _messages.IndexOf(message);

	internal int FindMessageIndex(Predicate<ChatMessageModel> predicate) => _messages.FindIndex(predicate);

	internal void AddMessage(ChatMessageModel message)
	{
		_messages.Add(message);
		_structuralVersion++;
	}

	internal void InsertMessage(int index, ChatMessageModel message)
	{
		_messages.Insert(index, message);
		_structuralVersion++;
	}

	internal bool RemoveMessage(ChatMessageModel message)
	{
		if(!_messages.Remove(message))
		{
			return false;
		}

		_structuralVersion++;
		return true;
	}

	internal ChatMessageModel RemoveMessageAt(int index)
	{
		ChatMessageModel message = _messages[index];
		_messages.RemoveAt(index);
		_structuralVersion++;
		return message;
	}

	/// <summary>
	/// Publishes the current mutable message collection as an immutable render snapshot.
	/// Existing callers may already hold <see cref="SyncRoot"/>; the lock is re-entrant.
	/// </summary>
	internal void PublishMessagesSnapshot()
	{
		lock(SyncRoot)
		{
			MessagesSnapshot = [.. _messages];
			_publishedStructuralVersion = _structuralVersion;
		}
	}

	/// <summary>
	/// Publishes a new immutable view only when controlled structural mutations occurred.
	/// Content-only mutations retain the existing snapshot instance.
	/// </summary>
	internal bool PublishMessagesSnapshotIfChanged()
	{
		lock(SyncRoot)
		{
			if(_publishedStructuralVersion == _structuralVersion)
			{
				return false;
			}

			MessagesSnapshot = [.. _messages];
			_publishedStructuralVersion = _structuralVersion;
			return true;
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
			_messagesView = _messages.AsReadOnly();
			_structuralVersion++;
			MessagesSnapshot = [.. _messages];
			_publishedStructuralVersion = _structuralVersion;
		}
	}

	/// <summary>
	/// Clears mutable history and publishes an empty snapshot as one transition.
	/// </summary>
	internal void ClearMessages()
	{
		lock(SyncRoot)
		{
			bool hadMessages = _messages.Count > 0;
			_messages.Clear();
			_messages.TrimExcess();
			if(hadMessages)
			{
				_structuralVersion++;
			}
			MessagesSnapshot = [];
			_publishedStructuralVersion = _structuralVersion;
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
