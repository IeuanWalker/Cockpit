namespace Cockpit.Features.Permissions.Models;

/// <summary>
/// Specifies the possible outcomes of a permission request, indicating whether access is denied or granted for varying
/// scopes.
/// </summary>
/// <remarks>Use this enumeration to determine how long a granted permission should remain effective. The
/// available options allow for denying access, granting it for a single execution, for the duration of the current
/// session, or globally across application restarts.</remarks>
public enum PermissionDecisionEnum
{
	/// <summary>
	/// Permission denied.
	/// </summary>
	Denied,
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
