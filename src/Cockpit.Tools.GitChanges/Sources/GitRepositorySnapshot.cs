using System.Collections.Immutable;

namespace Cockpit.Tools.GitChanges.Sources;

public enum GitChangeStatus { Modified, Added, Deleted, Renamed, Untracked }

public sealed record GitRepositoryIdentity(
	string CanonicalRoot,
	string? Origin,
	string? Head);

public sealed record GitChangedFileSnapshot
{
	public required string RelativePath { get; init; }
	public string? PreviousRelativePath { get; init; }
	public required GitChangeStatus Status { get; init; }
	public string? Diff { get; init; }
	public string? ContentFingerprint { get; init; }
	public string Name => Path.GetFileName(RelativePath);
}

public sealed record GitRepositorySnapshot
{
	public required string RuntimeSessionKey { get; init; }
	public required long IdentityEpoch { get; init; }
	public required long Generation { get; init; }
	public required GitRepositoryIdentity Repository { get; init; }
	public required DateTimeOffset CapturedAt { get; init; }
	public ImmutableArray<GitChangedFileSnapshot> Files { get; init; } = [];
}

public sealed record GitSourceChange(
	string RuntimeSessionKey,
	long IdentityEpoch,
	long RepositoryGeneration);

public sealed record GitAvailability(bool IsAvailable, string? Reason, GitRepositoryIdentity? Repository);
