using Cockpit.Models;

namespace Cockpit.Features.Permissions;

/// <summary>
/// Provides access to session state for permission handling
/// </summary>
public interface ISessionStateProvider
{
	/// <summary>
	/// Get all chat sessions
	/// </summary>
	IReadOnlyList<ChatSession> GetSessions();

	/// <summary>
	/// Notify that session state has changed
	/// </summary>
	void NotifyStateChanged();
}
