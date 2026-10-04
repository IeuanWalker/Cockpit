using System.Text.Json;

namespace Cockpit.Tooling;

public sealed record ToolAssetReference(string Id, string? MediaType = null, long? Length = null);

public sealed record ToolStateReadContext(
	int SchemaVersion,
	long MutationVersion,
	WorkspaceToolInstanceId SourceInstanceId,
	JsonElement Metadata,
	IReadOnlyList<ToolAssetReference> Assets,
	Func<ToolAssetReference, CancellationToken, ValueTask<Stream>> OpenAssetAsync);

public sealed record ToolStateWriteContext(
	WorkspaceToolInstanceId DestinationInstanceId,
	Func<string?, string?, CancellationToken, ValueTask<ToolAssetWriter>> CreateAssetAsync);

public sealed class ToolAssetWriter : IAsyncDisposable
{
	readonly Func<CancellationToken, ValueTask<ToolAssetReference>> _commitAsync;
	int _committed;

	public ToolAssetWriter(Stream stream, Func<CancellationToken, ValueTask<ToolAssetReference>> commitAsync)
	{
		Stream = stream ?? throw new ArgumentNullException(nameof(stream));
		_commitAsync = commitAsync ?? throw new ArgumentNullException(nameof(commitAsync));
	}

	public Stream Stream { get; }

	public ValueTask<ToolAssetReference> CommitAsync(CancellationToken cancellationToken = default)
	{
		if(Interlocked.Exchange(ref _committed, 1) != 0)
		{
			throw new InvalidOperationException("The staged asset has already been committed.");
		}

		return _commitAsync(cancellationToken);
	}

	public async ValueTask DisposeAsync() => await Stream.DisposeAsync().ConfigureAwait(false);
}

public sealed record ToolCheckpoint(
	int SchemaVersion,
	long MutationVersion,
	JsonElement Metadata,
	IReadOnlyList<ToolAssetReference> Assets,
	ToolRestorationFidelity Fidelity = ToolRestorationFidelity.Exact,
	IReadOnlyList<string>? Diagnostics = null);

public enum ToolRestorationFidelity { Exact, Partial, ConfigurationOnly }

public enum ToolRestoreStatus
{
	Restored,
	Migrated,
	PartiallyRestored,
	UnsupportedVersion,
	Invalid,
	NeedsUserInput
}

public sealed record ToolRestoreResult(
	ToolRestoreStatus Status,
	IReadOnlyList<string>? Diagnostics = null)
{
	public static ToolRestoreResult Restored() => new(ToolRestoreStatus.Restored);
}

public sealed record ToolStateChanged(
	WorkspaceToolInstanceId InstanceId,
	long MutationVersion,
	WorkspaceViewGeneration? Generation = null);

public enum ToolTemplateExportStatus { Prepared, Partial, Unsupported, Failed }

public sealed record ToolTemplateExportContext(
	WorkspaceToolInstanceId DestinationInstanceId,
	ToolStateWriteContext Destination,
	IReadOnlyDictionary<string, object?> Options);

public sealed record ToolTemplateExportResult(
	ToolTemplateExportStatus Status,
	ToolCheckpoint? Seed,
	IReadOnlyList<string>? Included = null,
	IReadOnlyList<string>? Transformed = null,
	IReadOnlyList<string>? Excluded = null,
	string? Message = null);

public enum ToolStateClonePurpose { NewSession, Duplicate }

public sealed record ToolStateCloneContext(
	ToolStateClonePurpose Purpose,
	WorkspaceToolInstanceId DestinationInstanceId,
	WorkspaceSessionInfo DestinationSession,
	ToolStateWriteContext Destination);

public enum ToolStateCloneStatus { Prepared, Deferred, NeedsRecovery, Failed }

public sealed record ToolStateCloneResult(
	ToolStateCloneStatus Status,
	ToolCheckpoint? Checkpoint,
	string? Message = null);
