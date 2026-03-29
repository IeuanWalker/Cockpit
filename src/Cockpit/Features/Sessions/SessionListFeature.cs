using Cockpit.Features.Sessions.Models;
using Microsoft.Extensions.Logging;

namespace Cockpit.Features.Sessions;

public class SessionListFeature : ISessionStateProvider
{
	readonly ILogger<SessionListFeature> _logger;
	readonly List<SessionModel> _sessions = [];

	public SessionListFeature(ILogger<SessionListFeature> logger)
	{
		_logger = logger;
	}

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

	// Coalesce rapid burst notifications into a single render frame (~60 fps cap).
	int _notifyPending = 0;

	public void NotifyStateChanged()
	{
		if(Interlocked.CompareExchange(ref _notifyPending, 1, 0) == 0)
		{
			_ = NotifyStateChangedAsync();
		}
	}

	async Task NotifyStateChangedAsync()
	{
		try
		{
			await Task.Delay(16).ConfigureAwait(false);
			Interlocked.Exchange(ref _notifyPending, 0);
			OnStateChanged?.Invoke();
		}
		catch(Exception ex)
		{
			// Swallow exceptions to prevent unobserved task exceptions from crashing the app.
			// OnStateChanged handlers are UI update callbacks; failures here are non-critical.
			_logger.LogDebug(ex, "StateChanged notification threw");
		}
	}
}
