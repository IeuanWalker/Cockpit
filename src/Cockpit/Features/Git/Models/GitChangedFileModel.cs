namespace Cockpit.Features.Git.Models;

public class GitChangedFileModel
{
	public required string Name { get; init; }
	public required string Path { get; init; }
	public GitFileStatusEnum Status { get; init; }
	public string? Diff { get; init; }
}

public enum GitFileStatusEnum
{
	Modified,
	Added,
	Deleted,
	Renamed,
	Untracked
}
