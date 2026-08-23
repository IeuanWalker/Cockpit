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
	public event Action<SessionStateChange>? OnSessionStateChanged;

	public IReadOnlyList<SessionModel> Sessions => _sessions;

	public SessionModel? CurrentSession { get; private set; }

	public void SetCurrentSession(SessionModel? session)
	{
		CurrentSession = session;
		NotifyStateChanged(session?.Id, SessionChangeKind.CurrentSession);
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

		bool removedCurrentSession = CurrentSession?.Id == sessionId;
		if(removedCurrentSession)
		{
			CurrentSession = null;
		}

		SessionChangeKind kind = SessionChangeKind.SessionCollection;
		if(removedCurrentSession)
		{
			kind |= SessionChangeKind.CurrentSession;
		}
		NotifyStateChanged(sessionId, kind);
	}

	// Coalesce rapid burst notifications into a single render frame (~60 fps cap).
	readonly Lock _notificationLock = new();
	readonly Dictionary<string, SessionChangeKind> _pendingSessionChanges = [];
	SessionChangeKind _pendingGlobalChanges;
	bool _notifyPending;

	public void NotifyStateChanged() => NotifyStateChanged(null, SessionChangeKind.All);

	public void NotifyStateChanged(string? sessionId, SessionChangeKind kind) => NotifyStateChanged(new SessionStateChange(sessionId, kind));

	public void NotifyStateChanged(SessionStateChange change)
	{
		if(change.Kind == SessionChangeKind.None)
		{
			return;
		}

		bool startNotifier = false;
		lock(_notificationLock)
		{
			if(change.SessionId is null)
			{
				_pendingGlobalChanges |= change.Kind;
			}
			else
			{
				_pendingSessionChanges.TryGetValue(change.SessionId, out SessionChangeKind pending);
				_pendingSessionChanges[change.SessionId] = pending | change.Kind;
			}

			if(!_notifyPending)
			{
				_notifyPending = true;
				startNotifier = true;
			}
		}

		if(startNotifier)
		{
			_ = NotifyStateChangedAsync();
		}
	}

	async Task NotifyStateChangedAsync()
	{
		try
		{
			await Task.Delay(16, CancellationToken.None).ConfigureAwait(false);

			SessionChangeKind globalChanges;
			KeyValuePair<string, SessionChangeKind>[] sessionChanges;
			lock(_notificationLock)
			{
				globalChanges = _pendingGlobalChanges;
				_pendingGlobalChanges = SessionChangeKind.None;
				sessionChanges = [.. _pendingSessionChanges];
				_pendingSessionChanges.Clear();
				_notifyPending = false;
			}

			if(globalChanges != SessionChangeKind.None)
			{
				InvokeTypedHandlers(new SessionStateChange(null, globalChanges));
			}
			foreach(KeyValuePair<string, SessionChangeKind> sessionChange in sessionChanges)
			{
				InvokeTypedHandlers(new SessionStateChange(sessionChange.Key, sessionChange.Value));
			}
			InvokeCompatibilityHandlers();
		}
		catch(Exception ex)
		{
			// Swallow exceptions to prevent unobserved task exceptions from crashing the app.
			// OnStateChanged handlers are UI update callbacks; failures here are non-critical.
			_logger.LogDebug(ex, "StateChanged notification threw");
		}
	}

	void InvokeTypedHandlers(SessionStateChange change)
	{
		Action<SessionStateChange>? handlers = OnSessionStateChanged;
		if(handlers is null)
		{
			return;
		}

		foreach(Action<SessionStateChange> handler in handlers.GetInvocationList().Cast<Action<SessionStateChange>>())
		{
			try
			{
				handler(change);
			}
			catch(Exception ex)
			{
				_logger.LogDebug(ex, "Typed state change handler threw");
			}
		}
	}

	void InvokeCompatibilityHandlers()
	{
		Action? handlers = OnStateChanged;
		if(handlers is null)
		{
			return;
		}

		foreach(Action handler in handlers.GetInvocationList().Cast<Action>())
		{
			try
			{
				handler();
			}
			catch(Exception ex)
			{
				_logger.LogDebug(ex, "StateChanged notification handler threw");
			}
		}
	}
}
