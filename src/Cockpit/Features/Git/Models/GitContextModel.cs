namespace Cockpit.Features.Git.Models;

public record GitContext
{
	public string? GitRoot { get; init; }
	public string? Repository { get; init; }
	public string? Branch { get; init; }
	public bool IsGitRepo => GitRoot is not null;
}
