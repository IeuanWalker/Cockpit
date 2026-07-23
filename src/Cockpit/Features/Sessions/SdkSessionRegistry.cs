using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using GitHub.Copilot;

namespace Cockpit.Features.Sessions;

/// <summary>
/// Owns the live SDK session instances and pairs event subscription with registration.
/// A single <see cref="CopilotSession"/> is registered here as soon as it is created or
/// resumed, and removed when it is disposed or deleted.
/// </summary>
public sealed class SdkSessionRegistry
{
	readonly ConcurrentDictionary<string, CopilotSession> _sessions = new();
	readonly Lock _syncRoot = new();

	/// <summary>
	/// Registers an SDK session and subscribes <paramref name="onEvent"/> to its event stream.
	/// The caller must remove an existing SDK session before registering its replacement.
	/// </summary>
	public void Register(CopilotSession session, Action<SessionEvent> onEvent)
	{
		lock(_syncRoot)
		{
			if(_sessions.ContainsKey(session.SessionId))
			{
				throw new InvalidOperationException($"An SDK session is already registered for {session.SessionId}");
			}

			session.On<SessionEvent>(onEvent);
			if(!_sessions.TryAdd(session.SessionId, session))
			{
				throw new InvalidOperationException($"Failed to register SDK session {session.SessionId}");
			}
		}
	}

	/// <summary>Returns the live SDK session for <paramref name="sessionId"/>, or <c>false</c> if not registered.</summary>
	public bool TryGet(string sessionId, [NotNullWhen(true)] out CopilotSession? session)
		=> _sessions.TryGetValue(sessionId, out session);

	/// <summary>Returns whether <paramref name="session"/> is still the registered instance for its ID.</summary>
	public bool IsCurrent(CopilotSession session)
		=> _sessions.TryGetValue(session.SessionId, out CopilotSession? registeredSession)
			&& ReferenceEquals(registeredSession, session);

	/// <summary>Removes and returns the SDK session. Returns <c>false</c> if not registered.</summary>
	public bool TryRemove(string sessionId, [NotNullWhen(true)] out CopilotSession? session)
	{
		lock(_syncRoot)
		{
			return _sessions.TryRemove(sessionId, out session);
		}
	}

	/// <summary>
	/// Removes the SDK session only when the registered instance is the expected instance.
	/// This prevents stale cleanup from removing a newer session registered under the same ID.
	/// </summary>
	public bool TryRemove(string sessionId, CopilotSession expectedSession)
	{
		lock(_syncRoot)
		{
			if(!_sessions.TryGetValue(sessionId, out CopilotSession? registeredSession)
				|| !ReferenceEquals(registeredSession, expectedSession))
			{
				return false;
			}

			return _sessions.TryRemove(sessionId, out _);
		}
	}

	/// <summary>Removes the entry for <paramref name="sessionId"/> (no-op if absent).</summary>
	public void Remove(string sessionId)
	{
		lock(_syncRoot)
		{
			_sessions.TryRemove(sessionId, out _);
		}
	}
}
