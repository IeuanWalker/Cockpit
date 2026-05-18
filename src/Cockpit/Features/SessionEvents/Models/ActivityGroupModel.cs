namespace Cockpit.Features.SessionEvents.Models;

public sealed class ActivityGroupModel
{
	public string Id { get; set; } = Guid.NewGuid().ToString();
	public DateTime StartTime { get; set; } = DateTime.MinValue;
	public DateTime? EndTime { get; set; }
	public bool IsExpanded { get; set; } = false;
	public List<ThinkingEventModel> Events { get; set; } = []; // Chronological list of messages and tools
	public GroupStatusEnum Status { get; set; } = GroupStatusEnum.Running;
	public string? InitialMessageId { get; set; } // Track the initial assistant message to insert after it
	public string? TriggeredByUserMessageId { get; set; } // Track the user message that triggered this turn

	/// <summary>
	/// <see langword="true"/> when this group is a temporary placeholder created to keep the
	/// working panel visible during the brief gap between one turn ending and the next
	/// <c>assistant.turn_start</c> firing (e.g. after an enqueued or immediate message).
	/// Replaced by the real group in <c>AssistantTurnStartHandler</c>.
	/// </summary>
	public bool IsPlaceholder { get; set; }

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