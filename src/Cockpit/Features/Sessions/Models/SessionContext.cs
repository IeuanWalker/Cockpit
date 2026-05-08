using Cockpit.Features.Agents.Models;
using Cockpit.Features.Git.Models;

namespace Cockpit.Features.Sessions.Models;

public class SessionContext
{
	public required string CurrentWorkingDirectory { get; set; }
	public required string? WorkspacePath { get; set; }
	public required string? GitRoot { get; set; }
	public required string? Repository { get; set; }
	public required string? Branch { get; set; }
	public List<GitChangedFileModel> EditedFiles { get; set; } = [];
	public List<string> AllowedCommands { get; set; } = [];
	public List<string> SessionPermissionCommands { get; set; } = [];
	public readonly Lock SessionPermissionCommandsLock = new();

	/// <summary>
	/// Custom agents discovered from the repo's .github/agents/ directory.
	/// </summary>
	public List<AgentProfile> RepoAgents { get; set; } = [];

	/// <summary>
	/// The currently selected agent for this session. Null means default Copilot behaviour.
	/// </summary>
	public AgentProfile? SelectedAgent { get; set; }

	/// <summary>
	/// The session-level agent mode (interactive, plan, or autopilot).
	/// </summary>
	public SessionAgentModeEnum SelectedAgentMode { get; set; } = SessionAgentModeEnum.Interactive;
}