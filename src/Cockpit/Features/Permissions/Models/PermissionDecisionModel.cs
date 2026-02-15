namespace Cockpit.Features.Permissions.Models;

/// <summary>
/// User's decision on a permission request
/// </summary>
public class PermissionDecisionModel
{
	/// <summary>
	/// Whether the user approved or denied the request
	/// </summary>
	public bool IsApproved { get; set; }

	/// <summary>
	/// Scope of the approval (if approved)
	/// </summary>
	public PermissionScope Scope { get; set; } = PermissionScope.Once;
}
