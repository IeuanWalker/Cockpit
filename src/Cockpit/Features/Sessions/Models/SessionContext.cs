using Cockpit.Features.Git.Models;

namespace Cockpit.Features.Sessions.Models;

public class SessionContext
{
	public SessionContext()
	{
		AgentSkills = ["Code Generation", "File Operations", "Git Operations", "Web Search"];
		McpServerUrl = "localhost:8080";
		McpServerConnected = true;
	}
	public required string CurrentWorkingDirectory { get; set; }
	public required string? WorkspacePath { get; set; }
	public required string? GitRoot { get; set; }
	public required string? Repository { get; set; }
	public required string? Branch { get; set; }
	public List<GitChangedFileModel> EditedFiles { get; set; } = [];
	public List<string> AllowedCommands { get; set; } = [];
	public List<string> SessionPermissionCommands { get; set; } = [];
	public readonly Lock SessionPermissionCommandsLock = new();
	public List<string> AgentSkills { get; set; } = [];
	public string McpServerUrl { get; set; } = "localhost:8080";
	public bool McpServerConnected { get; set; } = true;
}