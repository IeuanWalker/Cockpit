using System.Text.RegularExpressions;

namespace Cockpit.Tooling;

public sealed record WorkspaceToolDescriptor
{
	static readonly Regex toolIdPattern = new(
		"^[a-z][a-z0-9]*(?:[.-][a-z0-9]+)*$",
		RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

	public required string ToolId { get; init; }
	public required Version ApiVersion { get; init; }
	public required string DisplayName { get; init; }
	public string? Description { get; init; }
	public string? Icon { get; init; }
	public required int StateSchemaVersion { get; init; }
	public required WorkspaceToolContentDescriptor Content { get; init; }
	public IReadOnlyList<string> FormerToolIds { get; init; } = [];
	public WorkspaceToolTemplateEditorDescriptor? TemplateEditor { get; init; }

	public IReadOnlyList<string> Validate()
	{
		List<string> errors = [];
		if(string.IsNullOrWhiteSpace(ToolId) || !toolIdPattern.IsMatch(ToolId))
		{
			errors.Add("ToolId must be a stable lower-case dotted or dashed identifier.");
		}

		if(ApiVersion.Major <= 0)
		{
			errors.Add("ApiVersion must have a positive major version.");
		}

		if(string.IsNullOrWhiteSpace(DisplayName))
		{
			errors.Add("DisplayName is required.");
		}

		if(StateSchemaVersion < 0)
		{
			errors.Add("StateSchemaVersion cannot be negative.");
		}

		if(Content is null)
		{
			errors.Add("Content is required.");
		}

		if(FormerToolIds.Any(id => string.Equals(id, ToolId, StringComparison.Ordinal)))
		{
			errors.Add("FormerToolIds cannot contain the current ToolId.");
		}

		if(FormerToolIds.Any(id => string.IsNullOrWhiteSpace(id) || !toolIdPattern.IsMatch(id)))
		{
			errors.Add("FormerToolIds contains an invalid identifier.");
		}

		if(FormerToolIds.Distinct(StringComparer.Ordinal).Count() != FormerToolIds.Count)
		{
			errors.Add("FormerToolIds cannot contain duplicates.");
		}

		return errors;
	}
}

public abstract record WorkspaceToolContentDescriptor
{
	WorkspaceToolContentDescriptor() { }

	public sealed record Razor(Type ComponentType) : WorkspaceToolContentDescriptor;
	public sealed record Native(Type FactoryType) : WorkspaceToolContentDescriptor;
}

public sealed record WorkspaceToolTemplateEditorDescriptor(Type ComponentType);

/// <summary>Implemented by Razor tool components with a public Context parameter.</summary>
public interface IWorkspaceToolComponent;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class WorkspaceToolIgnoreAttribute : Attribute;
