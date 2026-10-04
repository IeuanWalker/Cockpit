using System.Collections.Immutable;
using Cockpit.Tools.GitChanges.Sources;

namespace Cockpit.Tools.GitChanges.State;

public enum GitComparisonKind { WorkingTree }

public sealed record GitComparison(GitComparisonKind Kind = GitComparisonKind.WorkingTree, string? BaseRef = null, string? TargetRef = null);

public sealed record GitFileSelection(string RelativePath, string? PreviousRelativePath, GitChangeStatus? Status);

public sealed record GitHunkAnchor(string Signature, string Side, int? Line);

public sealed record GitScrollAnchor(string? FileKey, string? HunkSignature, double Offset);

public sealed record CachedGitDisplay
{
	public required GitRepositoryIdentity Repository { get; init; }
	public required ImmutableArray<GitChangedFileSnapshot> Listing { get; init; }
	public GitChangedFileSnapshot? DisplayedFile { get; init; }
	public string? DiffAssetId { get; init; }
	public required DateTimeOffset CapturedAt { get; init; }
}

public sealed record GitChangesState
{
	public const int CurrentSchemaVersion = 1;
	public int SchemaVersion { get; init; } = CurrentSchemaVersion;
	public GitRepositoryBinding RepositoryBinding { get; init; } = GitRepositoryBinding.SessionRepository;
	public GitRepositoryIdentity? RepositoryIdentity { get; init; }
	public GitComparison Comparison { get; init; } = new();
	public GitFileSelection? SelectedFile { get; init; }
	public GitHunkAnchor? SelectedHunk { get; init; }
	public bool TreeView { get; init; }
	public bool SplitDiffView { get; init; }
	public bool IgnoreWhitespace { get; init; }
	public ImmutableHashSet<string> ExpandedDirectoryKeys { get; init; }
		= ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal);
	public bool DirectoryExpansionInitialized { get; init; }
	public double? FileListWidth { get; init; }
	public GitScrollAnchor? ScrollAnchor { get; init; }
	public string? FilterText { get; init; }
	public CachedGitDisplay? CachedDisplay { get; init; }
}
