namespace Cockpit.Tools.GitChanges.Sources;

/// <summary>Host adapter for immutable, session-bound repository data.</summary>
public interface IGitChangesToolSource
{
	ValueTask<GitAvailability> GetAvailabilityAsync(
		string runtimeSessionKey,
		long identityEpoch,
		string? repositoryRoot,
		CancellationToken cancellationToken);

	ValueTask<GitRepositorySnapshot?> GetSnapshotAsync(
		string runtimeSessionKey,
		long identityEpoch,
		GitRepositoryBinding binding,
		CancellationToken cancellationToken);

	IDisposable Subscribe(
		string runtimeSessionKey,
		long identityEpoch,
		Action<GitSourceChange> onChanged);

	ValueTask<bool> RevealFileAsync(
		string runtimeSessionKey,
		long identityEpoch,
		string canonicalRepositoryRoot,
		string relativePath,
		CancellationToken cancellationToken);
}

public enum GitRepositoryBindingKind { SessionRepository, ApprovedExplicitRoot }

public sealed record GitRepositoryBinding(GitRepositoryBindingKind Kind, string? ApprovedRoot = null)
{
	public static GitRepositoryBinding SessionRepository { get; } = new(GitRepositoryBindingKind.SessionRepository);
}
