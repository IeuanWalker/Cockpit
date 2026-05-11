namespace Cockpit.Features.Agents.Models;

public sealed class AgentProfile
{
	public required string Name { get; set; }
	public string? DisplayName { get; set; }
	public string? Description { get; set; }
	public string? FilePath { get; set; }
	public required AgentSource Source { get; set; }
	public bool UserInvocable { get; set; } = true;

	public string DisplayLabel => DisplayName ?? Name;
}
