using System.Collections.Concurrent;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging;

namespace CopilotGUI.Services.Copilot;

public class CopilotSessionManager
{
	readonly CopilotClientService _clientService;
	readonly ILogger<CopilotSessionManager> _logger;
	readonly ConcurrentDictionary<string, CopilotSession> _sessions = new();

	public event Action<string, SessionEvent>? OnSessionEvent;

	public CopilotSessionManager(CopilotClientService clientService, ILogger<CopilotSessionManager> logger)
	{
		_clientService = clientService;
		_logger = logger;
	}

	public async Task<CopilotSession> CreateSessionAsync(
		SessionConfig? config = null,
		CancellationToken cancellationToken = default)
	{
		try
		{
			CopilotClient client = await _clientService.GetClientAsync(cancellationToken);
			CopilotSession session = await client.CreateSessionAsync(config, cancellationToken);

			_sessions.TryAdd(session.SessionId, session);

			// Subscribe to session events
			session.On(evt =>
			{
				_logger.LogDebug("Session {SessionId} event: {EventType}", session.SessionId, evt.Type);
				OnSessionEvent?.Invoke(session.SessionId, evt);
			});

			_logger.LogInformation("Created session {SessionId}", session.SessionId);
			return session;
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Failed to create session");
			throw;
		}
	}

	public async Task<CopilotSession> ResumeSessionAsync(
		string sessionId,
		ResumeSessionConfig? config = null,
		CancellationToken cancellationToken = default)
	{
		try
		{
			CopilotClient client = await _clientService.GetClientAsync(cancellationToken);
			CopilotSession session = await client.ResumeSessionAsync(sessionId, config, cancellationToken);

			_sessions.AddOrUpdate(session.SessionId, session, (_, _) => session);

			// Subscribe to session events
			session.On(evt =>
			{
				_logger.LogDebug("Session {SessionId} event: {EventType}", session.SessionId, evt.Type);
				OnSessionEvent?.Invoke(session.SessionId, evt);
			});

			_logger.LogInformation("Resumed session {SessionId}", session.SessionId);
			return session;
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Failed to resume session {SessionId}", sessionId);
			throw;
		}
	}

	public async Task<List<SessionMetadata>> ListSessionsAsync(CancellationToken cancellationToken = default)
	{
		try
		{
			CopilotClient client = await _clientService.GetClientAsync(cancellationToken);
			return await client.ListSessionsAsync(cancellationToken);
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Failed to list sessions");
			return [];
		}
	}

	public async Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default)
	{
		try
		{
			if(_sessions.TryRemove(sessionId, out CopilotSession? session))
			{
				await session.DisposeAsync();
			}

			CopilotClient client = await _clientService.GetClientAsync(cancellationToken);
			await client.DeleteSessionAsync(sessionId, cancellationToken);

			_logger.LogInformation("Deleted session {SessionId}", sessionId);
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Failed to delete session {SessionId}", sessionId);
			throw;
		}
	}

	public bool TryGetSession(string sessionId, out CopilotSession? session)
	{
		return _sessions.TryGetValue(sessionId, out session);
	}

	public async Task<string> SendMessageAsync(
		string sessionId,
		string prompt,
		List<UserMessageDataAttachmentsItem>? attachments = null,
		CancellationToken cancellationToken = default)
	{
		if(!_sessions.TryGetValue(sessionId, out CopilotSession? session))
		{
			throw new InvalidOperationException($"Session {sessionId} not found");
		}

		try
		{
			string messageId = await session.SendAsync(new MessageOptions
			{
				Prompt = prompt,
				Attachments = attachments
			}, cancellationToken);

			_logger.LogInformation("Sent message to session {SessionId}: {MessageId}", sessionId, messageId);
			return messageId;
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Failed to send message to session {SessionId}", sessionId);
			throw;
		}
	}

	public async Task AbortSessionAsync(string sessionId, CancellationToken cancellationToken = default)
	{
		if(!_sessions.TryGetValue(sessionId, out CopilotSession? session))
		{
			throw new InvalidOperationException($"Session {sessionId} not found");
		}

		try
		{
			await session.AbortAsync(cancellationToken);
			_logger.LogInformation("Aborted session {SessionId}", sessionId);
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Failed to abort session {SessionId}", sessionId);
			throw;
		}
	}

	public async Task<IReadOnlyList<SessionEvent>> GetMessagesAsync(
		string sessionId,
		CancellationToken cancellationToken = default)
	{
		if(!_sessions.TryGetValue(sessionId, out CopilotSession? session))
		{
			throw new InvalidOperationException($"Session {sessionId} not found");
		}

		try
		{
			return await session.GetMessagesAsync(cancellationToken);
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Failed to get messages for session {SessionId}", sessionId);
			throw;
		}
	}

	public async ValueTask DisposeAllSessionsAsync()
	{
		foreach(CopilotSession session in _sessions.Values)
		{
			try
			{
				await session.DisposeAsync();
			}
			catch(Exception ex)
			{
				_logger.LogWarning(ex, "Error disposing session {SessionId}", session.SessionId);
			}
		}
		_sessions.Clear();
	}
}
