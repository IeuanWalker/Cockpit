using GitHub.Copilot.SDK;

namespace Cockpit.Features.Agents.Models;

public class AgentProfile
{
	public required CustomAgentConfig Config { get; set; }
	public required AgentSource Source { get; set; }
	public required string FilePath { get; set; }

	public string DisplayLabel => Config.DisplayName ?? Config.Name;
}
