namespace Cockpit.Features.SessionEvents.Models;

public class ChatMessageModel
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
}

public enum MessageTypeEnum
{
	Text,
	Code,
	Typing,
	ToolExecution,
	ToolResult,
	SystemMessage,
	Error,
	Reasoning,
	ActivityGroup
}