namespace Cockpit.Features.SessionEvents.Models;

public class ToolExecutionModel
{
	public string Id { get; set; } = Guid.NewGuid().ToString();
	public string ToolName { get; set; } = string.Empty;
	public string? ToolCallId { get; set; }
	public Dictionary<string, object>? InputParameters { get; set; }
	public string? Output { get; set; }
	public string? ProgressMessage { get; set; }
	public required DateTime StartTime { get; set; }
	public DateTime? EndTime { get; set; }
	public ToolStatusEnum Status { get; set; } = ToolStatusEnum.Running;
	public bool IsExpanded { get; set; } = false;
	public bool IsSuccess { get; set; } = true;
	readonly Lock _rawEventsLock = new();
	readonly List<Lazy<string>> _rawEvents = [];

	public void AddRawEvent(Lazy<string> rawEvent)
	{
		lock(_rawEventsLock)
		{
			_rawEvents.Add(rawEvent);
		}
	}

	public List<Lazy<string>> GetRawEventsSnapshot()
	{
		lock(_rawEventsLock)
		{
			return [.. _rawEvents];
		}
	}

	readonly Lock _childEventsLock = new();
	readonly List<ThinkingEventModel> _childEvents = [];

	public void AddChildEvent(ThinkingEventModel evt)
	{
		lock(_childEventsLock)
		{
			_childEvents.Add(evt);
		}
	}

	public void AddChild(ToolExecutionModel child)
	{
		AddChildEvent(new ThinkingEventModel
		{
			Type = ThinkingEventTypeEnum.Tool,
			Tool = child,
			Timestamp = child.StartTime,
			EventJson = child.GetRawEventsSnapshot()
		});
	}

	public List<ThinkingEventModel> GetChildEventsSnapshot()
	{
		lock(_childEventsLock)
		{
			return [.. _childEvents];
		}
	}

	public List<ToolExecutionModel> GetChildrenSnapshot()
	{
		lock(_childEventsLock)
		{
			return [.. _childEvents
				.Where(e => e.Type == ThinkingEventTypeEnum.Tool && e.Tool is not null)
				.Select(e => e.Tool!)];
		}
	}
}

public enum ToolStatusEnum
{
	Running,
	Success,
	Error
}