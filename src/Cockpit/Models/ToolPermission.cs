namespace Cockpit.Models;

/// <summary>
/// Represents a permission rule for tool execution
/// </summary>
public class ToolPermission
{
	/// <summary>
	/// The pattern to match against (e.g., "git status", "^npm (install|test)$", "rm -rf")
	/// </summary>
	public required string Pattern { get; set; }

	/// <summary>
	/// Type of pattern matching to use
	/// </summary>
	public PatternType Type { get; set; } = PatternType.Exact;

	/// <summary>
	/// Whether this permission allows or denies execution
	/// </summary>
	public bool IsAllowed { get; set; } = true;

	/// <summary>
	/// Scope of this permission (Global or Session)
	/// </summary>
	public PermissionScope Scope { get; set; } = PermissionScope.Global;

	/// <summary>
	/// When this permission was created
	/// </summary>
	public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

	/// <summary>
	/// Optional description/note about this permission
	/// </summary>
	public string? Description { get; set; }
}

/// <summary>
/// Type of pattern matching for permission rules
/// </summary>
public enum PatternType
{
	/// <summary>
	/// Exact string match
	/// </summary>
	Exact,

	/// <summary>
	/// Regular expression match
	/// </summary>
	Regex,

	/// <summary>
	/// String contains match
	/// </summary>
	Contains,

	/// <summary>
	/// String starts with match
	/// </summary>
	StartsWith
}
