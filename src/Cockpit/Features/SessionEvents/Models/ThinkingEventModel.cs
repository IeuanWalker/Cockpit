using Cockpit.Features.SessionEvents.Models.Enums;

namespace Cockpit.Features.SessionEvents.Models;

public class ThinkingEventModel
{
	public string? Id { get; set; }
	public DateTime Timestamp { get; set; } = DateTime.Now;
	public ThinkingEventTypeEnum Type { get; set; }
	public string? Message { get; set; } // For message events
	public ToolExecutionModel? Tool { get; set; } // For tool events
}