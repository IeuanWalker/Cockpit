namespace Cockpit.Features.Permissions.Models;

/// <summary>
/// Defines the scope of a permission decision
/// </summary>
public enum PermissionScope
{
	/// <summary>
	/// Allow only for this single execution
	/// </summary>
	Once,

	/// <summary>
	/// Allow for the current session only (cleared when session ends)
	/// </summary>
	Session,

	/// <summary>
	/// Allow globally and persist across app restarts
	/// </summary>
	Global
}
