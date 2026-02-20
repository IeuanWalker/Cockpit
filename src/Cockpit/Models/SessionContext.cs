namespace Cockpit.Models;

public class SessionContext
{
	public string CurrentDirectory { get; set; } = "/workspace/my-project";
	public string CurrentBranch { get; set; } = "main";
	public List<ContextFile> EditedFiles { get; set; } = [];
	public List<string> AllowedCommands { get; set; } = [];
	public List<string> SessionPermissionCommands { get; set; } = [];
	public readonly Lock SessionPermissionCommandsLock = new();
	public List<string> AgentSkills { get; set; } = [];
	public string McpServerUrl { get; set; } = "localhost:8080";
	public bool McpServerConnected { get; set; } = true;

	public static SessionContext CreateDefault(string? currentDirectory = null, string branch = "main")
	{
		return new SessionContext
		{
			CurrentDirectory = !string.IsNullOrWhiteSpace(currentDirectory)
				? currentDirectory
				: "/workspace/my-project",
			CurrentBranch = branch,
			EditedFiles =
			[
				new ContextFile { Name = "MyComponent.tsx", Path = "src/components/MyComponent.tsx", Status = FileStatus.Modified },
				new ContextFile { Name = "useCustomHook.ts", Path = "src/hooks/useCustomHook.ts", Status = FileStatus.Added },
				new ContextFile { Name = "App.tsx", Path = "src/App.tsx", Status = FileStatus.Modified },
				new ContextFile { Name = "OldComponent.jsx", Path = "src/components/OldComponent.jsx", Status = FileStatus.Deleted },
				new ContextFile { Name = "package.json", Path = "package.json", Status = FileStatus.Modified }
			],
			AllowedCommands = ["npm install", "git commit", "docker build"],
			SessionPermissionCommands = [],
			AgentSkills = ["Code Generation", "File Operations", "Git Operations", "Web Search"],
			McpServerUrl = "localhost:8080",
			McpServerConnected = true
		};
	}
}
