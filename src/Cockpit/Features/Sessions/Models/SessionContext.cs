namespace Cockpit.Features.Sessions.Models;

public class SessionContext
{
	public SessionContext()
	{
		// TODO: Remove temp data
		EditedFiles =
		[
			new SessionContextFileModel { Name = "MyComponent.tsx", Path = "src/components/MyComponent.tsx", Status = FileStatusEnum.Modified },
			new SessionContextFileModel { Name = "useCustomHook.ts", Path = "src/hooks/useCustomHook.ts", Status = FileStatusEnum.Added },
			new SessionContextFileModel { Name = "App.tsx", Path = "src/App.tsx", Status = FileStatusEnum.Modified },
			new SessionContextFileModel { Name = "OldComponent.jsx", Path = "src/components/OldComponent.jsx", Status = FileStatusEnum.Deleted },
			new SessionContextFileModel { Name = "package.json", Path = "package.json", Status = FileStatusEnum.Modified }
		];

		AgentSkills = ["Code Generation", "File Operations", "Git Operations", "Web Search"];
		McpServerUrl = "localhost:8080";
		McpServerConnected = true;
	}
	public required string CurrentWorkingDirectory { get; set; }
	public required string? WorkspacePath { get; set; }
	public required string? GitRoot { get; set; }
	public required string? Repository { get; set; }
	public required string? Branch { get; set; }
	public List<SessionContextFileModel> EditedFiles { get; set; } = [];
	public List<string> AllowedCommands { get; set; } = [];
	public List<string> SessionPermissionCommands { get; set; } = [];
	public readonly Lock SessionPermissionCommandsLock = new();
	public List<string> AgentSkills { get; set; } = [];
	public string McpServerUrl { get; set; } = "localhost:8080";
	public bool McpServerConnected { get; set; } = true;
}