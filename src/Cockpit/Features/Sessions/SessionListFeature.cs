using Cockpit.Models;
using Microsoft.Extensions.Logging;
using SessionContextModel = Cockpit.Models.SessionContext;

namespace Cockpit.Features.Sessions;

public class SessionListFeature : ISessionStateProvider
{
	readonly ILogger<SessionListFeature> _logger;

	readonly List<ChatSession> _sessions = [];

	public event Action? OnStateChanged;

	public IReadOnlyList<ChatSession> Sessions => _sessions;
	public ChatSession? CurrentSession { get; private set; }

	public SessionListFeature(ILogger<SessionListFeature> logger)
	{
		_logger = logger;
	}

	public void SetCurrentSession(ChatSession session)
	{
		session.Context ??= SessionContextModel.CreateDefault();
		CurrentSession = session;
		NotifyStateChanged();
	}

	internal void AddSession(ChatSession session)
	{
		_sessions.Insert(0, session);
	}

	internal void RemoveSession(string sessionId)
	{
		ChatSession? session = _sessions.FirstOrDefault(s => s.Id == sessionId);
		if(session is null)
		{
			return;
		}

		_sessions.Remove(session);

		if(CurrentSession?.Id == sessionId)
		{
			CurrentSession = _sessions.FirstOrDefault();
		}
	}

	public void NotifyStateChanged() => OnStateChanged?.Invoke();

	IReadOnlyList<ChatSession> ISessionStateProvider.GetSessions() => _sessions;
}
