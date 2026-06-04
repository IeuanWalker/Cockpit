namespace Cockpit.Features.SystemMessage;

/// <summary>
/// Stored override for a single system prompt section.
/// </summary>
public class SystemMessageSectionSetting
{
	public SystemMessageOverrideAction Action { get; set; } = SystemMessageOverrideAction.None;
	public string Content { get; set; } = string.Empty;
}
