using Cockpit.Features.Sessions.Models;

namespace Cockpit.Features.Sessions;

/// <summary>
/// Provides access to session state for permission handling
/// </summary>
public interface ISessionStateProvider
{
	/// <summary>
	/// Fired when session state changes
	/// </summary>
	event Action? OnStateChanged;

	/// <summary>
	/// Get all chat sessions
	/// </summary>
	IReadOnlyList<SessionModel> Sessions { get; }

	/// <summary>
	/// Notify that session state has changed
	/// </summary>
	void NotifyStateChanged();
}
