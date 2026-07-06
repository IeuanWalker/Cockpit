using Cockpit.Features.Sessions.Models;
using Microsoft.Extensions.Logging;

namespace Cockpit.Features.Sessions;

public sealed class SessionListFeature : ISessionStateProvider
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

	/// <summary>
	/// Inserts a batch of sessions at the front of the list in a single O(n) operation,
	/// preserving the same final ordering as calling <see cref="AddSession"/> for each
	/// session in <paramref name="sessions"/> order (i.e. the last item ends up first).
	/// Avoids the O(n²) cost of repeated <c>List.Insert(0, …)</c> shifts during bulk load.
	/// </summary>
	internal void AddSessionsAtFront(IReadOnlyList<SessionModel> sessions)
	{
		if(sessions.Count == 0)
		{
			return;
		}

		SessionModel[] reversed = new SessionModel[sessions.Count];
		for(int i = 0; i < sessions.Count; i++)
		{
			reversed[sessions.Count - 1 - i] = sessions[i];
		}

		_sessions.InsertRange(0, reversed);
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
			CurrentSession = null;
		}

		NotifyStateChanged();
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
			await Task.Delay(16, CancellationToken.None).ConfigureAwait(false);
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
