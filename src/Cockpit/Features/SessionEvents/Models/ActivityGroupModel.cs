namespace Cockpit.Features.SessionEvents.Models;

public class ActivityGroupModel
{
	public string Id { get; set; } = Guid.NewGuid().ToString();
	public DateTime StartTime { get; set; } = DateTime.Now;
	public DateTime? EndTime { get; set; }
	public bool IsExpanded { get; set; } = false;
	public List<ThinkingEventModel> Events { get; set; } = []; // Chronological list of messages and tools
	public GroupStatusEnum Status { get; set; } = GroupStatusEnum.Running;
	public string? InitialMessageId { get; set; } // Track the initial message to insert after it

	readonly Lock _eventsLock = new();

	// Thread-safe helper to get snapshot of events
	public List<ThinkingEventModel> GetEventsSnapshot()
	{
		lock(_eventsLock)
		{
			return [.. Events];
		}
	}

	// Thread-safe helper to add event
	public void AddEvent(ThinkingEventModel evt)
	{
		lock(_eventsLock)
		{
			Events.Add(evt);
		}
	}

	// Thread-safe helper to remove event
	public void RemoveEvent(ThinkingEventModel evt)
	{
		lock(_eventsLock)
		{
			Events.Remove(evt);
		}
	}

	// Helper to get just tools for summary
	public IEnumerable<ToolExecutionModel> Tools
	{
		get
		{
			lock(_eventsLock)
			{
				return Events
					.Where(e => e.Type == ThinkingEventTypeEnum.Tool && e.Tool is not null)
					.Select(e => e.Tool!)
					.ToList(); // Return a list to avoid deferred execution issues
			}
		}
	}
}

public enum GroupStatusEnum
{
	Running,
	Complete,
	Error
}