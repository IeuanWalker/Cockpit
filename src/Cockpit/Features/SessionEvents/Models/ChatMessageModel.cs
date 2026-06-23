using Cockpit.Features.Sessions.Models;

namespace Cockpit.Features.SessionEvents.Models;

public sealed class ChatMessageModel
{
	public string Id { get; set; } = Guid.NewGuid().ToString();
	public string Content { get; set; } = string.Empty;
	public bool IsUser { get; set; }
	public DateTimeOffset Timestamp { get; set; } = DateTime.Now;
	public MessageTypeEnum Type { get; set; } = MessageTypeEnum.Text;
	public string? EventType { get; set; }
	public string? ToolName { get; set; }
	public bool IsStreaming { get; set; }
	public bool IsComplete { get; set; } = true;
	public string? ReasoningContent { get; set; }
	public Dictionary<string, object>? Metadata { get; set; }
	public ActivityGroupModel? ActivityGroup { get; set; }
	/// <summary>
	/// True when this user message was sent while the agent was busy (confirmed by SDK but not yet processed).
	/// Cleared when the agent picks it up (AssistantTurnStartEvent).
	/// </summary>
	public bool IsPending { get; set; }

	/// <summary>
	/// True when this user message failed to send. Displays an error state with a retry button.
	/// </summary>
	public bool IsError { get; set; }

	/// <summary>
	/// True when this assistant text message was streamed to chat while no active working group
	/// existed (e.g. agent output emitted between the safety-net closing the prior group and
	/// <c>assistant.turn_start</c> creating the next one). These messages are candidates for
	/// absorption back into the new ops group by <c>AssistantTurnStartHandler</c>.
	/// </summary>
	public bool IsLeakedPreGroupMessage { get; set; }

	/// <summary>
	/// Attachments included with this user message, for display in the message bubble.
	/// </summary>
	public List<AttachmentModel>? Attachments { get; set; }

	public required List<Lazy<string>>? EventJson { get; set; }
}

public enum MessageTypeEnum
{
	Text,
	Error,
	Warning,
	ActivityGroup
}