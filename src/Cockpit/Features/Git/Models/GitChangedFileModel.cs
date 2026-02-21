namespace Cockpit.Features.Git.Models;

public class GitChangedFileModel
{
	public required string Name { get; init; }
	public required string Path { get; init; }
	public GitFileStatus Status { get; init; }
}

public enum GitFileStatus
{
	Modified,
	Added,
	Deleted,
	Renamed,
	Untracked
}
