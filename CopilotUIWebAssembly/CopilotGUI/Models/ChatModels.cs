namespace CopilotGUI.Models;

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
	public string? Model { get; set; }
	public string? ReasoningEffort { get; set; }
}

public enum SessionStatus
{
	Idle,
	Active,
	AgentRunning,
	AgentFinished,
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
	Reasoning
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
