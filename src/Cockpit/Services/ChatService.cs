using System.Diagnostics;
using Cockpit.Models;
using Cockpit.Services.Copilot;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging;

namespace Cockpit.Services;

public class ChatService
{
	readonly CopilotSessionManager _sessionManager;
	readonly ILogger<ChatService> _logger;
	readonly ContextService _contextService;
	readonly Dictionary<string, ChatMessage> _streamingMessages = [];

	public event Action? OnSessionsChanged;
	public event Action? OnMessagesChanged;
	public event Action<string>? OnError;
	public event Action? OnNewSessionRequested;

	public List<ChatSession> Sessions { get; private set; } = [];
	public ChatSession? CurrentSession { get; private set; }

	public ChatService(CopilotSessionManager sessionManager, ILogger<ChatService> logger, ContextService contextService)
	{
		_sessionManager = sessionManager;
		_logger = logger;
		_contextService = contextService;

		// Subscribe to session events
		_sessionManager.OnSessionEvent += HandleSessionEvent;
	}

	void HandleSessionEvent(string sessionId, SessionEvent evt)
	{
		ChatSession? session = Sessions.FirstOrDefault(s => s.Id == sessionId);
		if(session == null)
		{
			return;
		}

		try
		{
			switch(evt)
			{
				case UserMessageEvent userMsg:
					HandleUserMessage(session, userMsg);
					break;

				case AssistantMessageDeltaEvent deltaMsg:
					HandleAssistantMessageDelta(session, deltaMsg);
					break;

				case AssistantMessageEvent assistantMsg:
					HandleAssistantMessage(session, assistantMsg);
					break;

				case AssistantReasoningDeltaEvent reasoningDelta:
					HandleReasoningDelta(session, reasoningDelta);
					break;

				case AssistantReasoningEvent reasoning:
					HandleReasoning(session, reasoning);
					break;

				case ToolExecutionStartEvent toolStart:
					HandleToolStart(session, toolStart);
					break;

				case ToolExecutionCompleteEvent toolComplete:
					HandleToolComplete(session, toolComplete);
					break;

				case SessionIdleEvent:
					session.Status = SessionStatus.Idle;
					RemoveTypingIndicator(session);
					NotifyStateChanged();
					break;

				case SessionErrorEvent error:
					HandleSessionError(session, error);
					break;

				case SessionCompactionStartEvent:
					_logger.LogInformation("Session {SessionId} started context compaction", sessionId);
					break;

				case SessionCompactionCompleteEvent compaction:
					_logger.LogInformation("Session {SessionId} completed compaction: {TokensRemoved} tokens removed",
						sessionId, compaction.Data?.TokensRemoved);
					break;
			}
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Error handling session event {EventType} for session {SessionId}",
				evt.Type, sessionId);
		}
	}

	void HandleUserMessage(ChatSession session, UserMessageEvent evt)
	{
		Debug.WriteLine("HandleUserMessage");
		Debug.WriteLine(evt);

		if(evt.Data == null)
		{
			return;
		}

		ChatMessage message = new()
		{
			Id = Guid.NewGuid().ToString(),
			Content = evt.Data.Content ?? string.Empty,
			IsUser = true,
			Timestamp = DateTime.Now,
			Type = MessageType.Text,
			EventType = evt.Type
		};

		session.Messages.Add(message);
		session.LastActivity = DateTime.Now;
		session.Status = SessionStatus.AgentRunning;
		NotifyStateChanged();
	}

	void HandleAssistantMessageDelta(ChatSession session, AssistantMessageDeltaEvent evt)
	{
		Debug.WriteLine("HandleAssistantMessageDelta");
		Debug.WriteLine(evt);

		if(evt.Data == null)
		{
			return;
		}

		string messageId = evt.Data.MessageId ?? "streaming";

		if(!_streamingMessages.TryGetValue(messageId, out ChatMessage? message))
		{
			message = new ChatMessage
			{
				Id = messageId,
				Content = string.Empty,
				IsUser = false,
				Timestamp = DateTime.Now,
				Type = MessageType.Text,
				IsStreaming = true,
				IsComplete = false,
				EventType = evt.Type
			};
			_streamingMessages[messageId] = message;
			session.Messages.Add(message);
		}

		message.Content += evt.Data.DeltaContent ?? string.Empty;
		session.LastActivity = DateTime.Now;
		NotifyStateChanged();
	}

	void HandleAssistantMessage(ChatSession session, AssistantMessageEvent evt)
	{
		Debug.WriteLine("HandleAssistantMessage");
		Debug.WriteLine(evt);

		if(evt.Data == null)
		{
			return;
		}

		string messageId = evt.Data.MessageId ?? Guid.NewGuid().ToString();

		// If this was a streaming message, update it
		if(_streamingMessages.TryGetValue(messageId, out ChatMessage? existingMessage))
		{
			existingMessage.Content = evt.Data.Content ?? string.Empty;
			existingMessage.IsStreaming = false;
			existingMessage.IsComplete = true;
			_streamingMessages.Remove(messageId);
		}
		else
		{
			// Add as new message
			ChatMessage message = new()
			{
				Id = messageId,
				Content = evt.Data.Content ?? string.Empty,
				IsUser = false,
				Timestamp = DateTime.Now,
				Type = MessageType.Text,
				IsComplete = true,
				EventType = evt.Type
			};
			session.Messages.Add(message);
		}

		session.LastActivity = DateTime.Now;
		NotifyStateChanged();
	}

	void HandleReasoningDelta(ChatSession session, AssistantReasoningDeltaEvent evt)
	{
		Debug.WriteLine("HandleReasoningDelta");
		Debug.WriteLine(evt);

		if(evt.Data == null)
		{
			return;
		}

		string messageId = "reasoning";

		if(!_streamingMessages.TryGetValue(messageId, out ChatMessage? message))
		{
			message = new ChatMessage
			{
				Id = messageId,
				Content = string.Empty,
				ReasoningContent = string.Empty,
				IsUser = false,
				Timestamp = DateTime.Now,
				Type = MessageType.Reasoning,
				IsStreaming = true,
				IsComplete = false,
				EventType = evt.Type
			};
			_streamingMessages[messageId] = message;
			session.Messages.Add(message);
		}

		message.ReasoningContent += evt.Data.DeltaContent ?? string.Empty;
		session.LastActivity = DateTime.Now;
		NotifyStateChanged();
	}

	void HandleReasoning(ChatSession session, AssistantReasoningEvent evt)
	{
		Debug.WriteLine("HandleReasoning");
		Debug.WriteLine(evt);

		if(evt.Data == null)
		{
			return;
		}

		string messageId = "reasoning";

		if(_streamingMessages.TryGetValue(messageId, out ChatMessage? existingMessage))
		{
			existingMessage.ReasoningContent = evt.Data.Content ?? string.Empty;
			existingMessage.IsStreaming = false;
			existingMessage.IsComplete = true;
			_streamingMessages.Remove(messageId);
		}

		session.LastActivity = DateTime.Now;
		NotifyStateChanged();
	}

	void HandleToolStart(ChatSession session, ToolExecutionStartEvent evt)
	{
		Debug.WriteLine("HandleToolStart");
		Debug.WriteLine(evt);

		if(evt.Data == null)
		{
			return;
		}

		ChatMessage message = new()
		{
			Id = evt.Data.ToolCallId ?? Guid.NewGuid().ToString(),
			Content = $"Executing tool: {evt.Data.ToolName}",
			IsUser = false,
			Timestamp = DateTime.Now,
			Type = MessageType.ToolExecution,
			ToolName = evt.Data.ToolName,
			IsComplete = false,
			EventType = evt.Type,
			Metadata = []
		};

		session.Messages.Add(message);
		session.LastActivity = DateTime.Now;
		NotifyStateChanged();
	}

	void HandleToolComplete(ChatSession session, ToolExecutionCompleteEvent evt)
	{
		Debug.WriteLine("HandleToolComplete");
		Debug.WriteLine(evt);

		if(evt.Data == null)
		{
			return;
		}

		ChatMessage? toolMessage = session.Messages.FirstOrDefault(m =>
			m.Id == evt.Data.ToolCallId && m.Type == MessageType.ToolExecution);

		if(toolMessage != null)
		{
			toolMessage.IsComplete = true;
			toolMessage.Content = $"Tool '{toolMessage.ToolName}' completed";
			if(evt.Data.Result != null)
			{
				toolMessage.Metadata ??= [];
				toolMessage.Metadata["result"] = evt.Data.Result;
			}
		}

		session.LastActivity = DateTime.Now;
		NotifyStateChanged();
	}

	void HandleSessionError(ChatSession session, SessionErrorEvent evt)
	{
		Debug.WriteLine("HandleSessionError");
		Debug.WriteLine(evt);

		if(evt.Data == null)
		{
			return;
		}

		ChatMessage message = new()
		{
			Id = Guid.NewGuid().ToString(),
			Content = evt.Data.Message ?? "An error occurred",
			IsUser = false,
			Timestamp = DateTime.Now,
			Type = MessageType.Error,
			EventType = evt.Type
		};

		session.Messages.Add(message);
		session.Status = SessionStatus.Error;
		session.LastActivity = DateTime.Now;

		OnError?.Invoke(evt.Data.Message ?? "Unknown error");
		NotifyStateChanged();
	}

	public void RequestNewSession()
	{
		OnNewSessionRequested?.Invoke();
	}

	public async Task LoadExistingSessionsAsync()
	{
		try
		{
			_logger.LogInformation("Loading existing sessions from SDK...");

			List<SessionMetadata> sessionMetadataList = await _sessionManager.ListSessionsAsync();

			if(sessionMetadataList.Count == 0)
			{
				_logger.LogInformation("No existing sessions found");
				return;
			}

			_logger.LogInformation("Found {Count} existing sessions", sessionMetadataList.Count);

			// Load each session that isn't already in our list
			foreach(SessionMetadata metadata in sessionMetadataList)
			{
				if(!Sessions.Any(s => s.Id == metadata.SessionId))
				{
					try
					{
						ChatSession chatSession = new()
						{
							Id = metadata.SessionId,
							Title = metadata.Summary ?? $"Session {metadata.SessionId[..8]}",
							CreatedAt = metadata.StartTime,
							LastActivity = metadata.ModifiedTime,
							Status = SessionStatus.Idle
						};

						Sessions.Add(chatSession);
						_logger.LogInformation("Loaded session {SessionId}", chatSession.Id);
					}
					catch(Exception ex)
					{
						_logger.LogWarning(ex, "Failed to load session {SessionId}", metadata.SessionId);
					}
				}
			}

			NotifyStateChanged();
			_logger.LogInformation("Successfully loaded {Count} sessions", Sessions.Count);
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Failed to load existing sessions");
			OnError?.Invoke($"Failed to load existing sessions: {ex.Message}");
		}
	}

	public async Task<ChatSession> CreateNewSessionAsync(ModelInfo? model = null, string? reasoningEffort = null, string? workingDirectory = null)
	{
		try
		{
			SessionConfig config = new()
			{
				Model = model?.Id,
				ReasoningEffort = reasoningEffort,
				Streaming = true,
				InfiniteSessions = new InfiniteSessionConfig { Enabled = true },
				WorkingDirectory = workingDirectory
			};

			CopilotSession sdkSession = await _sessionManager.CreateSessionAsync(config);

			ChatSession chatSession = new()
			{
				Id = sdkSession.SessionId,
				Title = !string.IsNullOrEmpty(workingDirectory)
					? Path.GetFileName(workingDirectory)
					: "New Session",
				CreatedAt = DateTime.Now,
				LastActivity = DateTime.Now,
				Status = SessionStatus.Idle,
				WorkspacePath = sdkSession.WorkspacePath,
				WorkingDirectory = workingDirectory,
				Model = model?.Id,
				ReasoningEffort = reasoningEffort
			};

			Sessions.Insert(0, chatSession);
			CurrentSession = chatSession;

			// Update the context service with the working directory
			if(!string.IsNullOrEmpty(workingDirectory))
			{
				_contextService.SetDirectory(workingDirectory);
			}

			NotifyStateChanged();
			return chatSession;
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Failed to create new session");
			OnError?.Invoke($"Failed to create session: {ex.Message}");
			throw;
		}
	}

	public async Task<bool> ResumeSessionAsync(string sessionId)
	{
		try
		{
			ChatSession? session = Sessions.FirstOrDefault(s => s.Id == sessionId);
			if(session == null)
			{
				_logger.LogWarning("Session {SessionId} not found", sessionId);
				return false;
			}

			_logger.LogInformation("Resuming session {SessionId}", sessionId);

			ResumeSessionConfig config = new()
			{
				Model = session.Model,
				ReasoningEffort = session.ReasoningEffort,
				Streaming = true
			};

			CopilotSession sdkSession = await _sessionManager.ResumeSessionAsync(sessionId, config);

			// Load existing messages from SDK
			IReadOnlyList<SessionEvent> events = await _sessionManager.GetMessagesAsync(sessionId);
			session.Messages.Clear();

			_logger.LogInformation("Loading {Count} events for session {SessionId}", events.Count, sessionId);

			foreach(SessionEvent evt in events)
			{
				// Convert SDK events to chat messages
				if(evt is UserMessageEvent userMsg && userMsg.Data != null)
				{
					session.Messages.Add(new ChatMessage
					{
						Id = Guid.NewGuid().ToString(),
						Content = userMsg.Data.Content ?? string.Empty,
						IsUser = true,
						Timestamp = userMsg.Timestamp,
						Type = MessageType.Text
					});
				}
				else if(evt is AssistantMessageEvent assistantMsg && assistantMsg.Data != null)
				{
					session.Messages.Add(new ChatMessage
					{
						Id = Guid.NewGuid().ToString(),
						Content = assistantMsg.Data.Content ?? string.Empty,
						IsUser = false,
						Timestamp = assistantMsg.Timestamp,
						Type = MessageType.Text
					});
				}
			}

			session.Status = SessionStatus.Idle;
			session.WorkspacePath = sdkSession.WorkspacePath;
			CurrentSession = session;

			// Update context service with working directory
			if(!string.IsNullOrEmpty(session.WorkingDirectory))
			{
				_contextService.SetDirectory(session.WorkingDirectory);
			}
			else if(!string.IsNullOrEmpty(session.WorkspacePath))
			{
				_contextService.SetDirectory(session.WorkspacePath);
			}

			NotifyStateChanged();
			_logger.LogInformation("Successfully resumed session {SessionId} with {MessageCount} messages",
				sessionId, session.Messages.Count);
			return true;
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Failed to resume session {SessionId}", sessionId);
			OnError?.Invoke($"Failed to resume session: {ex.Message}");
			return false;
		}
	}

	public void SetCurrentSession(ChatSession session)
	{
		CurrentSession = session;

		// Update context service when switching sessions
		if(!string.IsNullOrEmpty(session.WorkingDirectory))
		{
			_contextService.SetDirectory(session.WorkingDirectory);
		}
		else if(!string.IsNullOrEmpty(session.WorkspacePath))
		{
			// Fallback to workspace path if no working directory is set
			_contextService.SetDirectory(session.WorkspacePath);
		}

		NotifyMessagesChanged();
	}

	public async Task SendMessageAsync(string content, List<UserMessageDataAttachmentsItem>? attachments = null)
	{
		if(CurrentSession == null)
		{
			return;
		}

		try
		{
			// Add typing indicator
			AddTypingIndicator(CurrentSession);

			CurrentSession.Status = SessionStatus.AgentRunning;
			await _sessionManager.SendMessageAsync(CurrentSession.Id, content, attachments);
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Failed to send message");
			OnError?.Invoke($"Failed to send message: {ex.Message}");
			RemoveTypingIndicator(CurrentSession);
			CurrentSession.Status = SessionStatus.Error;
			NotifyStateChanged();
		}
	}

	public async Task DeleteSessionAsync(string sessionId)
	{
		try
		{
			await _sessionManager.DeleteSessionAsync(sessionId);

			ChatSession? session = Sessions.FirstOrDefault(s => s.Id == sessionId);
			if(session != null)
			{
				Sessions.Remove(session);
				if(CurrentSession?.Id == sessionId)
				{
					CurrentSession = Sessions.FirstOrDefault();
				}
				NotifyStateChanged();
			}
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Failed to delete session {SessionId}", sessionId);
			OnError?.Invoke($"Failed to delete session: {ex.Message}");
		}
	}

	public async Task AbortCurrentSessionAsync()
	{
		if(CurrentSession == null)
		{
			return;
		}

		try
		{
			await _sessionManager.AbortSessionAsync(CurrentSession.Id);
			CurrentSession.Status = SessionStatus.Idle;
			RemoveTypingIndicator(CurrentSession);
			NotifyStateChanged();
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Failed to abort session");
			OnError?.Invoke($"Failed to abort: {ex.Message}");
		}
	}

	void AddTypingIndicator(ChatSession session)
	{
		ChatMessage typingMessage = new()
		{
			Content = string.Empty,
			IsUser = false,
			Type = MessageType.Typing,
			Timestamp = DateTime.Now
		};

		session.Messages.Add(typingMessage);
		NotifyMessagesChanged();
	}

	void RemoveTypingIndicator(ChatSession session)
	{
		ChatMessage? typingMessage = session.Messages.FirstOrDefault(m => m.Type == MessageType.Typing);
		if(typingMessage != null)
		{
			session.Messages.Remove(typingMessage);
			NotifyMessagesChanged();
		}
	}

	void NotifyStateChanged()
	{
		OnSessionsChanged?.Invoke();
		OnMessagesChanged?.Invoke();
	}

	void NotifyMessagesChanged() => OnMessagesChanged?.Invoke();
}

