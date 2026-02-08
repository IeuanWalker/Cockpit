using CopilotGUI.Models;

namespace CopilotGUI.Services;

public class ChatService
{
	public event Action? OnSessionsChanged;
	public event Action? OnMessagesChanged;

	public List<ChatSession> Sessions { get; private set; } = [];
	public ChatSession? CurrentSession { get; private set; }

	// Sample data
	public ChatService()
	{
		InitializeSampleData();
	}

	void InitializeSampleData()
	{
		ChatSession activeSession = new()
		{
			Title = "React Component Refactor",
			CreatedAt = DateTime.Now.AddMinutes(-5),
			LastActivity = DateTime.Now,
			Status = SessionStatus.Active,
			Messages =
			[
				new ChatMessage
				{
					Content = "Can you help me refactor this React component to use hooks instead of class components?",
					IsUser = true,
					Timestamp = DateTime.Now.AddMinutes(-5)
				},
				new ChatMessage
				{
					Content = "I'll help you refactor your class component to use React hooks. Here's how we can convert it:\n\n```csharp\nimport React, { useState, useEffect } from 'react';\n\nconst MyComponent = () => {\n  const [data, setData] = useState([]);\n  const [loading, setLoading] = useState(false);\n\n  useEffect(() => {\n    fetchData();\n  }, []);\n\n  return (\n    <div>{/* component JSX */}</div>\n  );\n};\n```\n\nThe main changes are:\n- Replaced this.state with useState hooks\n- Used useEffect for lifecycle methods\n- Converted to functional component syntax",
					IsUser = false,
					Timestamp = DateTime.Now.AddMinutes(-4)
				},
				new ChatMessage
				{
					Content = "Perfect! Can you also show me how to handle the component's previous lifecycle methods?",
					IsUser = true,
					Timestamp = DateTime.Now.AddSeconds(-10)
				}
			]
		};

		Sessions.Add(activeSession);
		Sessions.Add(new ChatSession
		{
			Title = "API Integration Help",
			Status = SessionStatus.AgentRunning,
			CreatedAt = DateTime.Now.AddMinutes(-15),
			LastActivity = DateTime.Now.AddMinutes(-2)
		});
		Sessions.Add(new ChatSession
		{
			Title = "Database Schema Design",
			Status = SessionStatus.AgentFinished,
			CreatedAt = DateTime.Now.AddMinutes(-30),
			LastActivity = DateTime.Now.AddMinutes(-10)
		});
		Sessions.Add(new ChatSession
		{
			Title = "Bug Fix - Authentication",
			CreatedAt = DateTime.Now.AddDays(-2),
			LastActivity = DateTime.Now.AddDays(-2)
		});
		Sessions.Add(new ChatSession
		{
			Title = "TypeScript Migration",
			CreatedAt = DateTime.Now.AddDays(-7),
			LastActivity = DateTime.Now.AddDays(-7)
		});

		CurrentSession = activeSession;
	}

	public void SetCurrentSession(ChatSession session)
	{
		CurrentSession = session;
		NotifyMessagesChanged();
	}

	public void CreateNewSession()
	{
		ChatSession newSession = new()
		{
			Title = "New Session",
			CreatedAt = DateTime.Now,
			LastActivity = DateTime.Now
		};
		Sessions.Insert(0, newSession);
		CurrentSession = newSession;
		NotifySessionsChanged();
		NotifyMessagesChanged();
	}

	public void AddMessage(string content, bool isUser)
	{
		if(CurrentSession is null)
		{
			return;
		}

		ChatMessage message = new()
		{
			Content = content,
			IsUser = isUser,
			Timestamp = DateTime.Now
		};

		CurrentSession.Messages.Add(message);
		CurrentSession.LastActivity = DateTime.Now;
		NotifyMessagesChanged();
	}

	public void AddTypingIndicator()
	{
		if(CurrentSession is null)
		{
			return;
		}

		ChatMessage typingMessage = new()
		{
			Content = string.Empty,
			IsUser = false,
			Type = MessageType.Typing,
			Timestamp = DateTime.Now
		};

		CurrentSession.Messages.Add(typingMessage);
		NotifyMessagesChanged();
	}

	public void RemoveTypingIndicator()
	{
		if(CurrentSession is null)
		{
			return;
		}

		ChatMessage? typingMessage = CurrentSession.Messages.FirstOrDefault(m => m.Type == MessageType.Typing);
		if(typingMessage is not null)
		{
			CurrentSession.Messages.Remove(typingMessage);
			NotifyMessagesChanged();
		}
	}

	void NotifySessionsChanged() => OnSessionsChanged?.Invoke();
	void NotifyMessagesChanged() => OnMessagesChanged?.Invoke();
}
