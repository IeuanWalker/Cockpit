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

	/// <summary>
	/// Registers an SDK session and subscribes <paramref name="onEvent"/> to its event stream.
	/// Replaces any previously registered entry for the same session ID.
	/// </summary>
	public void Register(CopilotSession session, Action<SessionEvent> onEvent)
	{
		session.On<SessionEvent>(onEvent);
		_sessions[session.SessionId] = session;
	}

	/// <summary>Returns the live SDK session for <paramref name="sessionId"/>, or <c>false</c> if not registered.</summary>
	public bool TryGet(string sessionId, [NotNullWhen(true)] out CopilotSession? session)
		=> _sessions.TryGetValue(sessionId, out session);

	/// <summary>Removes and returns the SDK session. Returns <c>false</c> if not registered.</summary>
	public bool TryRemove(string sessionId, [NotNullWhen(true)] out CopilotSession? session)
		=> _sessions.TryRemove(sessionId, out session);

	/// <summary>Removes the entry for <paramref name="sessionId"/> (no-op if absent).</summary>
	public void Remove(string sessionId) => _sessions.TryRemove(sessionId, out _);
}
