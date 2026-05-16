namespace Cockpit.Features.SessionEvents.Models;

public sealed class ThinkingEventModel
{
	public string? Id { get; set; }
	public DateTime Timestamp { get; set; } = DateTime.Now;
	public ThinkingEventTypeEnum Type { get; set; }
	public string? Message { get; set; } // For message events
	public ToolExecutionModel? Tool { get; set; } // For tool events
	public required List<Lazy<string>>? EventJson { get; set; }
}

public enum ThinkingEventTypeEnum
{
	Message,
	Tool,
	Reasoning,
	UserMessage
}