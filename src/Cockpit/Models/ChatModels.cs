using System.Collections.Concurrent;
using Cockpit.Features.Permissions.Models;
using GitHub.Copilot.SDK;
using SessionContextModel = Cockpit.Models.SessionContext;

namespace Cockpit.Models;

public class ChatSession
{
	public string Id { get; set; } = Guid.NewGuid().ToString();
	public string Title { get; set; } = "New Session";
	public DateTime CreatedAt { get; set; } = DateTime.Now;
	public DateTime LastActivity { get; set; } = DateTime.Now;
	public SessionStatus Status { get; set; } = SessionStatus.Active;
	public List<ChatMessage> Messages { get; set; } = [];
	public string? WorkspacePath { get; set; }
	public string? WorkingDirectory { get; set; }
	public SessionContextModel Context { get; set; } = SessionContextModel.CreateDefault();
	public required ModelInfo Model { get; set; }
	public string? ReasoningEffort { get; set; }
	public ActivityGroup? ActiveWorkingGroup { get; set; }
	public Dictionary<string, ChatMessage> StreamingMessages { get; } = [];

	/// <summary>
	/// Pending permission requests for this session (supports multiple concurrent requests)
	/// Key: request.Id, Value: PermissionRequest
	/// </summary>
	public ConcurrentDictionary<string, PermissionRequestModel> PendingPermissionRequests { get; set; } = new();

	/// <summary>
	/// Lock for coordinating permission request status changes
	/// </summary>
	public readonly Lock PermissionRequestsLock = new();

	/// <summary>
	/// Previous status before permission request (to restore after all decisions)
	/// </summary>
	public SessionStatus? PreviousStatus { get; set; }

	/// <summary>
	/// Whether this session has an active SDK connection
	/// </summary>
	public bool IsResumed { get; set; }

	/// <summary>
	/// Whether this session requires a restart to apply configuration changes (model/reasoning effort)
	/// </summary>
	public bool RequiresRestart { get; set; }

	/// <summary>
	/// YOLO mode - automatically accept all permissions without prompting
	/// </summary>
	public bool IsYolo { get; set; }

	/// <summary>
	/// Terminal state for this session
	/// </summary>
	public bool IsTerminalOpen { get; set; }
}

public enum SessionStatus
{
	Idle,
	Active,
	Running,
	NeedsPermission,
	Finished,
	Error,
	Archived
}

public class ChatMessage
{
	public string Id { get; set; } = Guid.NewGuid().ToString();
	public string Content { get; set; } = string.Empty;
	public bool IsUser { get; set; }
	public DateTimeOffset Timestamp { get; set; } = DateTime.Now;
	public MessageType Type { get; set; } = MessageType.Text;
	public string? EventType { get; set; }
	public string? ToolName { get; set; }
	public bool IsStreaming { get; set; }
	public bool IsComplete { get; set; } = true;
	public string? ReasoningContent { get; set; }
	public Dictionary<string, object>? Metadata { get; set; }
	public ActivityGroup? ActivityGroup { get; set; }
}

public enum MessageType
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

// Thinking event - can be either a message or a tool execution
public class ThinkingEvent
{
	public string? Id { get; set; }
	public DateTime Timestamp { get; set; } = DateTime.Now;
	public ThinkingEventType Type { get; set; }
	public string? Message { get; set; } // For message events
	public ToolExecution? Tool { get; set; } // For tool events
}

public enum ThinkingEventType
{
	Message,
	Tool
}

// Activity group for multiple tool executions
public class ActivityGroup
{
	public string Id { get; set; } = Guid.NewGuid().ToString();
	public DateTime StartTime { get; set; } = DateTime.Now;
	public DateTime? EndTime { get; set; }
	public bool IsExpanded { get; set; } = false;
	public List<ThinkingEvent> Events { get; set; } = []; // Chronological list of messages and tools
	public GroupStatus Status { get; set; } = GroupStatus.Running;
	public string? InitialMessageId { get; set; } // Track the initial message to insert after it

	readonly Lock _eventsLock = new();

	// Thread-safe helper to get snapshot of events
	public List<ThinkingEvent> GetEventsSnapshot()
	{
		lock(_eventsLock)
		{
			return [.. Events];
		}
	}

	// Thread-safe helper to add event
	public void AddEvent(ThinkingEvent evt)
	{
		lock(_eventsLock)
		{
			Events.Add(evt);
		}
	}

	// Thread-safe helper to remove event
	public void RemoveEvent(ThinkingEvent evt)
	{
		lock(_eventsLock)
		{
			Events.Remove(evt);
		}
	}

	// Helper to get just tools for summary
	public IEnumerable<ToolExecution> Tools
	{
		get
		{
			lock(_eventsLock)
			{
				return Events
					.Where(e => e.Type == ThinkingEventType.Tool && e.Tool is not null)
					.Select(e => e.Tool!)
					.ToList(); // Return a list to avoid deferred execution issues
			}
		}
	}
}

// Individual tool execution
public class ToolExecution
{
	public string Id { get; set; } = Guid.NewGuid().ToString();
	public string ToolName { get; set; } = string.Empty;
	public string? ToolCallId { get; set; }
	public Dictionary<string, object>? InputParameters { get; set; }
	public string? Output { get; set; }
	public string? ProgressMessage { get; set; }
	public DateTime StartTime { get; set; } = DateTime.Now;
	public DateTime? EndTime { get; set; }
	public ToolStatus Status { get; set; } = ToolStatus.Running;
	public bool IsExpanded { get; set; } = false;
	public bool IsSuccess { get; set; } = true;
	public List<string> RawEvents { get; } = [];
}

public enum GroupStatus
{
	Running,
	Complete,
	Error
}

public enum ToolStatus
{
	Running,
	Success,
	Error
}

public class ContextFile
{
	public string Name { get; set; } = string.Empty;
	public string Path { get; set; } = string.Empty;
	public FileStatus Status { get; set; } = FileStatus.Unmodified;
}

public enum FileStatus
{
	Unmodified,
	Modified,
	Added,
	Deleted
}
