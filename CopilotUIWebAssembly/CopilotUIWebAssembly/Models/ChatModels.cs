namespace CopilotUIWebAssembly.Models;

public class ChatSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = "New Session";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime LastActivity { get; set; } = DateTime.Now;
    public SessionStatus Status { get; set; } = SessionStatus.Active;
    public List<ChatMessage> Messages { get; set; } = new();
}

public enum SessionStatus
{
    Active,
    AgentRunning,
    AgentFinished,
    Archived
}

public class ChatMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Content { get; set; } = string.Empty;
    public bool IsUser { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public MessageType Type { get; set; } = MessageType.Text;
}

public enum MessageType
{
    Text,
    Code,
    Typing
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
