using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Cockpit.Features.Workspace.Storage;

sealed class FileWorkspaceGenerationStore : IWorkspaceGenerationStore
{
	const string currentFileName = "current.json";
	const string lockFileName = "workspace.lock";
	public ValueTask<IWorkspaceWriteLease?> TryAcquireWriteLeaseAsync(string storageRoot, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		try
		{
			Directory.CreateDirectory(storageRoot);
			string lockPath = Path.Combine(storageRoot, lockFileName);
			FileStream stream = new(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read, 1, FileOptions.Asynchronous);
			return ValueTask.FromResult<IWorkspaceWriteLease?>(new FileWorkspaceWriteLease(Path.GetFullPath(storageRoot), stream));
		}
		catch(IOException)
		{
			return ValueTask.FromResult<IWorkspaceWriteLease?>(null);
		}
		catch(UnauthorizedAccessException)
		{
			return ValueTask.FromResult<IWorkspaceWriteLease?>(null);
		}
	}

	public async Task<WorkspaceLoadResult> LoadAsync(string storageRoot, CancellationToken cancellationToken)
	{
		string pointerPath = Path.Combine(storageRoot, currentFileName);
		if(!File.Exists(pointerPath))
		{
			return new WorkspaceLoadResult(WorkspaceLoadStatus.NotFound);
		}

		try
		{
			await using FileStream pointerStream = new(pointerPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
			WorkspaceCurrentPointer? pointer = await JsonSerializer.DeserializeAsync(pointerStream, WorkspaceJsonContext.Default.WorkspaceCurrentPointer, cancellationToken);
			if(pointer is null || !IsSafeSegment(pointer.GenerationId))
			{
				return new WorkspaceLoadResult(WorkspaceLoadStatus.Invalid, Diagnostic: "The current generation pointer is invalid.");
			}

			string manifestPath = Path.Combine(storageRoot, "generations", pointer.GenerationId, "manifest.json");
			await using FileStream manifestStream = new(manifestPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
			WorkspaceManifest? manifest = await JsonSerializer.DeserializeAsync(manifestStream, WorkspaceJsonContext.Default.WorkspaceManifest, cancellationToken);
			if(manifest is null)
			{
				return new WorkspaceLoadResult(WorkspaceLoadStatus.Invalid, Diagnostic: "The committed manifest is empty.");
			}
			if(manifest.SchemaVersion > WorkspaceManifest.currentSchemaVersion)
			{
				return new WorkspaceLoadResult(WorkspaceLoadStatus.UnsupportedVersion, Diagnostic: $"Schema {manifest.SchemaVersion} is newer than supported schema {WorkspaceManifest.currentSchemaVersion}.");
			}
			if(manifest.SchemaVersion <= 0)
			{
				return new WorkspaceLoadResult(WorkspaceLoadStatus.Invalid, Diagnostic: "The committed manifest schema is invalid.");
			}
			try
			{
				_ = manifest.ToState("manifest-validation");
			}
			catch(InvalidDataException exception)
			{
				return new WorkspaceLoadResult(WorkspaceLoadStatus.Invalid, Diagnostic: exception.Message);
			}
			return new WorkspaceLoadResult(WorkspaceLoadStatus.Restored, manifest, pointer.GenerationId);
		}
		catch(JsonException exception)
		{
			return new WorkspaceLoadResult(WorkspaceLoadStatus.Invalid, Diagnostic: exception.Message);
		}
		catch(FileNotFoundException exception)
		{
			return new WorkspaceLoadResult(WorkspaceLoadStatus.Invalid, Diagnostic: exception.Message);
		}
		catch(DirectoryNotFoundException exception)
		{
			return new WorkspaceLoadResult(WorkspaceLoadStatus.Invalid, Diagnostic: exception.Message);
		}
		catch(UnauthorizedAccessException exception)
		{
			return new WorkspaceLoadResult(WorkspaceLoadStatus.Unavailable, Diagnostic: exception.Message);
		}
		catch(IOException exception)
		{
			return new WorkspaceLoadResult(WorkspaceLoadStatus.Unavailable, Diagnostic: exception.Message);
		}
	}

	public async Task CommitAsync(IWorkspaceWriteLease lease, WorkspaceGenerationCommit generation, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(lease);
		ArgumentNullException.ThrowIfNull(generation);
		if(!IsSafeSegment(generation.GenerationId))
		{
			throw new ArgumentException("The generation ID must be a single safe path segment.", nameof(generation));
		}

		string storageRoot = Path.GetFullPath(lease.StorageRoot);
		string objectsRoot = Path.Combine(storageRoot, "objects");
		string generationsRoot = Path.Combine(storageRoot, "generations");
		Directory.CreateDirectory(objectsRoot);
		Directory.CreateDirectory(generationsRoot);

		foreach(WorkspaceObjectWrite objectWrite in generation.Objects)
		{
			await WriteObjectIfMissingAsync(objectsRoot, objectWrite, cancellationToken);
		}

		string generationPath = Path.Combine(generationsRoot, generation.GenerationId);
		Directory.CreateDirectory(generationPath);
		string manifestPath = Path.Combine(generationPath, "manifest.json");
		await WriteJsonFileAsync(manifestPath, generation.Manifest, WorkspaceJsonContext.Default.WorkspaceManifest, cancellationToken);

		WorkspaceCurrentPointer pointer = new() { GenerationId = generation.GenerationId };
		string temporaryPointer = Path.Combine(storageRoot, $".{currentFileName}.{Guid.NewGuid():N}.tmp");
		try
		{
			await WriteJsonFileAsync(temporaryPointer, pointer, WorkspaceJsonContext.Default.WorkspaceCurrentPointer, cancellationToken);
			File.Move(temporaryPointer, Path.Combine(storageRoot, currentFileName), true);
		}
		finally
		{
			if(File.Exists(temporaryPointer))
			{
				File.Delete(temporaryPointer);
			}
		}
	}

	public Task<Stream> OpenObjectReadAsync(string storageRoot, string contentId, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if(!WorkspaceContentId.IsValid(contentId))
		{
			throw new ArgumentException("The content ID is invalid.", nameof(contentId));
		}
		Stream stream = new FileStream(
			Path.Combine(storageRoot, "objects", contentId),
			FileMode.Open,
			FileAccess.Read,
			FileShare.Read | FileShare.Delete,
			64 * 1024,
			FileOptions.Asynchronous | FileOptions.SequentialScan);
		return Task.FromResult(stream);
	}

	static async Task WriteObjectIfMissingAsync(string objectsRoot, WorkspaceObjectWrite objectWrite, CancellationToken cancellationToken)
	{
		if(!WorkspaceContentId.IsValid(objectWrite.ContentId))
		{
			throw new InvalidDataException($"Content ID '{objectWrite.ContentId}' is not a SHA-256 ID.");
		}
		string destination = Path.Combine(objectsRoot, objectWrite.ContentId.ToLowerInvariant());
		if(File.Exists(destination))
		{
			return;
		}

		string temporary = Path.Combine(objectsRoot, $".{objectWrite.ContentId}.{Guid.NewGuid():N}.tmp");
		try
		{
			string actual;
			await using(Stream source = await objectWrite.OpenReadAsync(cancellationToken))
			await using(FileStream output = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
			{
				using System.Security.Cryptography.IncrementalHash hash =
					System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
				byte[] buffer = new byte[64 * 1024];
				int read;
				while((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
				{
					hash.AppendData(buffer, 0, read);
					await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
				}
				await output.FlushAsync(cancellationToken);
				output.Flush(true);
				actual = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
			}
			if(!string.Equals(actual, objectWrite.ContentId, StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidDataException($"Object '{objectWrite.ContentId}' has content hash '{actual}'.");
			}
			File.Move(temporary, destination, false);
		}
		catch(IOException) when(File.Exists(destination))
		{
			// A competing writer committed the same immutable object.
		}
		finally
		{
			if(File.Exists(temporary))
			{
				File.Delete(temporary);
			}
		}
	}

	static async Task WriteJsonFileAsync<T>(string path, T value, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken)
	{
		await using FileStream stream = new(path, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
		await JsonSerializer.SerializeAsync(stream, value, typeInfo, cancellationToken);
		await stream.FlushAsync(cancellationToken);
		stream.Flush(true);
	}

	static bool IsSafeSegment(string value)
		=> !string.IsNullOrWhiteSpace(value)
		&& value is not "." and not ".."
		&& value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
		&& !value.Contains(Path.DirectorySeparatorChar)
		&& !value.Contains(Path.AltDirectorySeparatorChar);

	sealed class FileWorkspaceWriteLease(string storageRoot, FileStream stream) : IWorkspaceWriteLease
	{
		public string StorageRoot { get; } = storageRoot;
		public async ValueTask DisposeAsync() => await stream.DisposeAsync();
	}
}

sealed class WorkspaceCurrentPointer
{
	public required string GenerationId { get; init; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(WorkspaceManifest))]
[JsonSerializable(typeof(WorkspaceCurrentPointer))]
sealed partial class WorkspaceJsonContext : JsonSerializerContext;
