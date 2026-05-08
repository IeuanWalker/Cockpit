using Cockpit.Features.Agents.Models;
using Cockpit.Features.Git.Models;
using GitHub.Copilot.SDK.Rpc;
using SdkPlugin = GitHub.Copilot.SDK.Rpc.Plugin;

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
	/// All agents discovered for this session via the SDK.
	/// </summary>
	public List<AgentProfile> Agents { get; set; } = [];

	/// <summary>
	/// The currently selected agent for this session. Null means default Copilot behaviour.
	/// </summary>
	public AgentProfile? SelectedAgent { get; set; }

	/// <summary>The session-level agent mode (interactive, plan, or autopilot).</summary>
	public SessionAgentModeEnum SelectedAgentMode { get; set; } = SessionAgentModeEnum.Interactive;

	/// <summary>All instruction sources discovered for this session via the SDK.</summary>
	public List<InstructionsSources> Instructions { get; set; } = [];

	/// <summary>All MCP servers discovered for this session via the SDK.</summary>
	public List<McpServer> McpServers { get; set; } = [];

	/// <summary>All skills discovered for this session via the SDK.</summary>
	public List<Skill> Skills { get; set; } = [];

	/// <summary>All plugins discovered for this session via the SDK.</summary>
	public List<SdkPlugin> Plugins { get; set; } = [];
}