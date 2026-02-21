namespace Cockpit.Features.Sessions.Models;

public class SessionContext
{
	public SessionContext()
	{
		// TODO: Remove temp data
		EditedFiles =
		[
			new ContextFile { Name = "MyComponent.tsx", Path = "src/components/MyComponent.tsx", Status = FileStatus.Modified },
			new ContextFile { Name = "useCustomHook.ts", Path = "src/hooks/useCustomHook.ts", Status = FileStatus.Added },
			new ContextFile { Name = "App.tsx", Path = "src/App.tsx", Status = FileStatus.Modified },
			new ContextFile { Name = "OldComponent.jsx", Path = "src/components/OldComponent.jsx", Status = FileStatus.Deleted },
			new ContextFile { Name = "package.json", Path = "package.json", Status = FileStatus.Modified }
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
	public List<ContextFile> EditedFiles { get; set; } = [];
	public List<string> AllowedCommands { get; set; } = [];
	public List<string> SessionPermissionCommands { get; set; } = [];
	public readonly Lock SessionPermissionCommandsLock = new();
	public List<string> AgentSkills { get; set; } = [];
	public string McpServerUrl { get; set; } = "localhost:8080";
	public bool McpServerConnected { get; set; } = true;
}

public class ContextFile
{
	public string Name { get; set; } = string.Empty;
	public string Path { get; set; } = string.Empty;
	public FileStatus Status { get; set; } = FileStatus.Unmodified;
}

public enum FileStatus
{
	Unmodified,
	Modified,
	Added,
	Deleted
}
