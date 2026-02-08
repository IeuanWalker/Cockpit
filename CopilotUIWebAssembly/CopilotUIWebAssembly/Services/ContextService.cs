using CopilotUIWebAssembly.Models;

namespace CopilotUIWebAssembly.Services;

public class ContextService
{
    public event Action? OnContextChanged;

    public string CurrentDirectory { get; private set; } = "/workspace/my-project";
    public string CurrentBranch { get; private set; } = "main";
    public List<string> Branches { get; private set; } = new() { "main", "feature/new-ui", "bugfix/auth-issue" };
    public List<ContextFile> EditedFiles { get; private set; } = new();
    public List<string> AllowedCommands { get; private set; } = new();
    public List<string> AgentSkills { get; private set; } = new();
    public string McpServerUrl { get; private set; } = "localhost:8080";
    public bool McpServerConnected { get; private set; } = true;

    public ContextService()
    {
        InitializeSampleData();
    }

    private void InitializeSampleData()
    {
        EditedFiles = new List<ContextFile>
        {
            new ContextFile { Name = "MyComponent.tsx", Path = "src/components/MyComponent.tsx", Status = FileStatus.Modified },
            new ContextFile { Name = "useCustomHook.ts", Path = "src/hooks/useCustomHook.ts", Status = FileStatus.Added },
            new ContextFile { Name = "App.tsx", Path = "src/App.tsx", Status = FileStatus.Modified },
            new ContextFile { Name = "OldComponent.jsx", Path = "src/components/OldComponent.jsx", Status = FileStatus.Deleted },
            new ContextFile { Name = "package.json", Path = "package.json", Status = FileStatus.Modified }
        };

        AllowedCommands = new List<string> { "npm install", "git commit", "docker build" };
        AgentSkills = new List<string> { "Code Generation", "File Operations", "Git Operations", "Web Search" };
    }

    public void SetDirectory(string directory)
    {
        CurrentDirectory = directory;
        NotifyContextChanged();
    }

    public void SetBranch(string branch)
    {
        CurrentBranch = branch;
        NotifyContextChanged();
    }

    public void ToggleCommand(string command)
    {
        if (AllowedCommands.Contains(command))
        {
            AllowedCommands.Remove(command);
        }
        else
        {
            AllowedCommands.Add(command);
        }
        NotifyContextChanged();
    }

    public void ToggleSkill(string skill)
    {
        if (AgentSkills.Contains(skill))
        {
            AgentSkills.Remove(skill);
        }
        else
        {
            AgentSkills.Add(skill);
        }
        NotifyContextChanged();
    }

    public bool IsCommandAllowed(string command) => AllowedCommands.Contains(command);
    public bool IsSkillEnabled(string skill) => AgentSkills.Contains(skill);

    private void NotifyContextChanged() => OnContextChanged?.Invoke();
}
