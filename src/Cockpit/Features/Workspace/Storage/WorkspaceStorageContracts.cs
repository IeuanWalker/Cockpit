namespace Cockpit.Features.Workspace.Storage;

enum WorkspaceLoadStatus
{
	Restored,
	NotFound,
	Invalid,
	UnsupportedVersion,
	Unavailable,
	InUse
}

sealed record WorkspaceLoadResult(
	WorkspaceLoadStatus Status,
	WorkspaceManifest? Manifest = null,
	string? GenerationId = null,
	string? Diagnostic = null);

sealed record WorkspaceObjectWrite(string ContentId, Func<CancellationToken, ValueTask<Stream>> OpenReadAsync);

sealed record WorkspaceGenerationCommit(
	string GenerationId,
	WorkspaceManifest Manifest,
	IReadOnlyCollection<WorkspaceObjectWrite> Objects);

interface IWorkspaceWriteLease : IAsyncDisposable
{
	string StorageRoot { get; }
}

interface IWorkspaceGenerationStore
{
	ValueTask<IWorkspaceWriteLease?> TryAcquireWriteLeaseAsync(string storageRoot, CancellationToken cancellationToken);
	Task<WorkspaceLoadResult> LoadAsync(string storageRoot, CancellationToken cancellationToken);
	Task CommitAsync(IWorkspaceWriteLease lease, WorkspaceGenerationCommit generation, CancellationToken cancellationToken);
	Task<Stream> OpenObjectReadAsync(string storageRoot, string contentId, CancellationToken cancellationToken);
}

static class WorkspaceContentId
{
	internal static async Task<string> ComputeAsync(Stream content, CancellationToken cancellationToken)
	{
		using System.Security.Cryptography.IncrementalHash hash =
			System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
		byte[] buffer = new byte[64 * 1024];
		int read;
		while((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
		{
			hash.AppendData(buffer, 0, read);
		}
		return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
	}

	internal static bool IsValid(string contentId)
		=> contentId.Length == 64 && contentId.All(character => char.IsAsciiHexDigit(character));
}
