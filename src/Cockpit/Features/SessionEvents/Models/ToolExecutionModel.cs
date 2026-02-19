namespace Cockpit.Features.SessionEvents.Models;

public class ToolExecutionModel
{
	public string Id { get; set; } = Guid.NewGuid().ToString();
	public string ToolName { get; set; } = string.Empty;
	public string? ToolCallId { get; set; }
	public Dictionary<string, object>? InputParameters { get; set; }
	public string? Output { get; set; }
	public string? ProgressMessage { get; set; }
	public DateTime StartTime { get; set; } = DateTime.Now;
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

	readonly Lock _childrenLock = new();
	readonly List<ToolExecutionModel> _children = [];

	public void AddChild(ToolExecutionModel child)
	{
		lock(_childrenLock)
		{
			_children.Add(child);
		}
	}

	public List<ToolExecutionModel> GetChildrenSnapshot()
	{
		lock(_childrenLock)
		{
			return [.. _children];
		}
	}
}

public enum ToolStatusEnum
{
	Running,
	Success,
	Error
}