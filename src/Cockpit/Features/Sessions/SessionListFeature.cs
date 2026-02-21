using Cockpit.Features.Sessions.Models;

namespace Cockpit.Features.Sessions;

public class SessionListFeature : ISessionStateProvider
{
	readonly List<SessionModel> _sessions = [];

	public event Action? OnStateChanged;

	public IReadOnlyList<SessionModel> Sessions => _sessions;

	public SessionModel? CurrentSession { get; private set; }

	public void SetCurrentSession(SessionModel session)
	{
		CurrentSession = session;
		NotifyStateChanged();
	}

	internal void AddSession(SessionModel session)
	{
		_sessions.Insert(0, session);
	}

	internal void RemoveSession(string sessionId)
	{
		SessionModel? session = _sessions.FirstOrDefault(s => s.Id == sessionId);
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

}
