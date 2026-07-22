using Cockpit.Features.SessionEvents.Models;

namespace Cockpit.Features.Sessions.Models;

/// <summary>
/// Compatibility surface for conversation consumers. These accessors forward to
/// <see cref="Conversation"/> and can be removed as consumers adopt explicit transitions.
/// </summary>
public partial class SessionModel
{
	public List<ChatMessageModel> Messages
	{
		get => Conversation.Messages;
		set => Conversation.Messages = value;
	}

	public IReadOnlyList<ChatMessageModel> MessagesSnapshot
	{
		get => Conversation.MessagesSnapshot;
		internal set => Conversation.MessagesSnapshot = value;
	}

	public ActivityGroupModel? ActiveWorkingGroup
	{
		get => Conversation.ActiveWorkingGroup;
		set => Conversation.ActiveWorkingGroup = value;
	}

	public Dictionary<string, ChatMessageModel> StreamingMessages => Conversation.StreamingMessages;
	public Dictionary<string, ThinkingEventModel> StreamingThinkingEvents => Conversation.StreamingThinkingEvents;

	public string? PendingTaskSummary
	{
		get => Conversation.PendingTaskSummary;
		set => Conversation.PendingTaskSummary = value;
	}

	public Lock SessionEventLock => Conversation.SyncRoot;

	public int PendingMessageCount
	{
		get => Conversation.PendingMessageCount;
		set => Conversation.PendingMessageCount = value;
	}

	public TokenUsageInfoModel? TokenUsageInfo
	{
		get => Conversation.TokenUsageInfo;
		set => Conversation.TokenUsageInfo = value;
	}

	public bool IsCompacting
	{
		get => Conversation.IsCompacting;
		set => Conversation.IsCompacting = value;
	}

	public bool AgentTurnCompleted
	{
		get => Conversation.AgentTurnCompleted;
		set => Conversation.AgentTurnCompleted = value;
	}

	public bool HasQueuedImmediateMessage
	{
		get => Conversation.HasQueuedImmediateMessage;
		set => Conversation.HasQueuedImmediateMessage = value;
	}
}
