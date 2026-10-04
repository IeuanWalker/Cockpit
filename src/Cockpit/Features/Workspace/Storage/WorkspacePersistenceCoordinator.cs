namespace Cockpit.Features.Workspace.Storage;

enum WorkspaceSaveStatus
{
	Saved,
	AlreadySaved,
	PartiallySaved,
	Suppressed,
	Failed
}

sealed record WorkspaceSaveResult(
	WorkspaceSaveStatus Status,
	long PersistedRevision,
	IReadOnlyList<string>? AffectedInstanceIds = null,
	string? Diagnostic = null);

sealed record WorkspacePersistenceSnapshot(
	WorkspaceManifest Manifest,
	long Revision,
	long IdentityEpoch,
	IReadOnlyDictionary<string, long> CheckpointVersions,
	IReadOnlyCollection<WorkspaceObjectWrite> Objects,
	IReadOnlyList<string>? FailedInstanceIds = null);

/// <summary>
/// Serialises manifest commits for one RuntimeSessionKey. Snapshot creation happens only after the
/// save gate is acquired, preventing an older queued whole-workspace snapshot from overwriting a
/// newer state. Tool checkpoint capture should finish before the snapshot is exposed here.
/// </summary>
sealed class WorkspacePersistenceCoordinator : IAsyncDisposable
{
	readonly IWorkspaceGenerationStore _store;
	readonly Func<CancellationToken, ValueTask<WorkspacePersistenceSnapshot>> _captureLatest;
	readonly SemaphoreSlim _saveGate = new(1, 1);
	readonly Lock _sync = new();
	readonly TimeSpan _debounce;
	IWorkspaceWriteLease _lease;
	readonly CancellationTokenSource _lifetime = new();
	Task? _scheduledSave;
	long _requestedRevision;
	long _persistedRevision;
	long _identityEpoch;
	bool _deleted;
	string? _lastFailure;

	WorkspacePersistenceCoordinator(
		IWorkspaceGenerationStore store,
		IWorkspaceWriteLease lease,
		long identityEpoch,
		long persistedRevision,
		Func<CancellationToken, ValueTask<WorkspacePersistenceSnapshot>> captureLatest,
		TimeSpan debounce)
	{
		_store = store;
		_lease = lease;
		_identityEpoch = identityEpoch;
		_persistedRevision = persistedRevision;
		_requestedRevision = persistedRevision;
		_captureLatest = captureLatest;
		_debounce = debounce;
	}

	internal static async Task<WorkspacePersistenceCoordinator?> TryCreateAsync(
		IWorkspaceGenerationStore store,
		string storageRoot,
		long identityEpoch,
		long persistedRevision,
		Func<CancellationToken, ValueTask<WorkspacePersistenceSnapshot>> captureLatest,
		TimeSpan? debounce = null,
		CancellationToken cancellationToken = default)
	{
		IWorkspaceWriteLease? lease = await store.TryAcquireWriteLeaseAsync(storageRoot, cancellationToken);
		return lease is null
			? null
			: new WorkspacePersistenceCoordinator(
				store,
				lease,
				identityEpoch,
				persistedRevision,
				captureLatest,
				debounce ?? TimeSpan.FromMilliseconds(500));
	}

	internal long PersistedRevision
	{
		get
		{
			lock(_sync)
			{
				return _persistedRevision;
			}
		}
	}

	internal void RequestSave(long revision)
	{
		lock(_sync)
		{
			if(_deleted)
			{
				return;
			}
			_requestedRevision = Math.Max(_requestedRevision, revision);
			if(_scheduledSave is null || _scheduledSave.IsCompleted)
			{
				_scheduledSave = RunDebouncedSaveAsync(_lifetime.Token);
			}
		}
	}

	internal async Task<WorkspaceSaveResult> FlushAsync(long targetRevision, CancellationToken cancellationToken = default)
	{
		lock(_sync)
		{
			if(_deleted)
			{
				return new WorkspaceSaveResult(WorkspaceSaveStatus.Suppressed, _persistedRevision, Diagnostic: "The workspace is pending deletion.");
			}
			_requestedRevision = Math.Max(_requestedRevision, targetRevision);
			if(_persistedRevision >= targetRevision)
			{
				return new WorkspaceSaveResult(WorkspaceSaveStatus.AlreadySaved, _persistedRevision);
			}
		}

		return await SaveUntilAsync(targetRevision, cancellationToken);
	}

	internal async Task<bool> RebindAsync(string storageRoot, long newIdentityEpoch, CancellationToken cancellationToken = default)
	{
		await _saveGate.WaitAsync(cancellationToken);
		try
		{
			lock(_sync)
			{
				if(_deleted || newIdentityEpoch <= _identityEpoch)
				{
					return false;
				}
			}

			IWorkspaceWriteLease? replacement = await _store.TryAcquireWriteLeaseAsync(storageRoot, cancellationToken);
			if(replacement is null)
			{
				return false;
			}

			IWorkspaceWriteLease previous = _lease;
			_lease = replacement;
			lock(_sync)
			{
				_identityEpoch = newIdentityEpoch;
				// A new destination needs a complete generation even when the logical revision did not move.
				_persistedRevision = -1;
				_requestedRevision = Math.Max(_requestedRevision, 0);
				_lastFailure = null;
			}
			await previous.DisposeAsync();
			return true;
		}
		finally
		{
			_saveGate.Release();
		}
	}

	internal async Task RevokeForDeletionAsync(CancellationToken cancellationToken = default)
	{
		lock(_sync)
		{
			_deleted = true;
			_lifetime.Cancel();
		}
		await _saveGate.WaitAsync(cancellationToken);
		try
		{
			await _lease.DisposeAsync();
		}
		finally
		{
			_saveGate.Release();
		}
	}

	async Task RunDebouncedSaveAsync(CancellationToken cancellationToken)
	{
		try
		{
			await Task.Delay(_debounce, cancellationToken);
			long target;
			lock(_sync)
			{
				target = _requestedRevision;
			}
			await SaveUntilAsync(target, cancellationToken);
		}
		catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested)
		{
		}
		catch(Exception exception)
		{
			lock(_sync)
			{
				_lastFailure = exception.Message;
			}
		}
	}

	async Task<WorkspaceSaveResult> SaveUntilAsync(long targetRevision, CancellationToken cancellationToken)
	{
		await _saveGate.WaitAsync(cancellationToken);
		try
		{
			while(true)
			{
				long epoch;
				lock(_sync)
				{
					if(_deleted)
					{
						return new WorkspaceSaveResult(WorkspaceSaveStatus.Suppressed, _persistedRevision, Diagnostic: "The workspace is pending deletion.");
					}
					if(_persistedRevision >= targetRevision)
					{
						return new WorkspaceSaveResult(WorkspaceSaveStatus.AlreadySaved, _persistedRevision);
					}
					epoch = _identityEpoch;
				}

				WorkspacePersistenceSnapshot snapshot;
				try
				{
					snapshot = await _captureLatest(cancellationToken);
				}
				catch(Exception exception) when(exception is not OperationCanceledException)
				{
					lock(_sync)
					{
						_lastFailure = exception.Message;
						return new WorkspaceSaveResult(WorkspaceSaveStatus.Failed, _persistedRevision, Diagnostic: exception.Message);
					}
				}

				lock(_sync)
				{
					if(_deleted || snapshot.IdentityEpoch != _identityEpoch || epoch != _identityEpoch)
					{
						continue;
					}
				}

				string generationId = $"{DateTimeOffset.UtcNow:yyyyMMddHHmmssfffffff}-{Guid.NewGuid():N}";
				try
				{
					await _store.CommitAsync(
						_lease,
						new WorkspaceGenerationCommit(generationId, snapshot.Manifest, snapshot.Objects),
						cancellationToken);
				}
				catch(Exception exception) when(exception is not OperationCanceledException)
				{
					lock(_sync)
					{
						_lastFailure = exception.Message;
						return new WorkspaceSaveResult(WorkspaceSaveStatus.Failed, _persistedRevision, Diagnostic: exception.Message);
					}
				}

				long requested;
				lock(_sync)
				{
					if(_deleted || snapshot.IdentityEpoch != _identityEpoch)
					{
						continue;
					}
					_persistedRevision = Math.Max(_persistedRevision, snapshot.Revision);
					_lastFailure = null;
					requested = _requestedRevision;
				}
				if(snapshot.Revision < targetRevision)
				{
					lock(_sync)
					{
						_scheduledSave = RunDebouncedSaveAsync(_lifetime.Token);
					}
					return new WorkspaceSaveResult(
						WorkspaceSaveStatus.PartiallySaved,
						snapshot.Revision,
						snapshot.FailedInstanceIds,
						"The target revision has not produced a complete checkpoint yet.");
				}

				if(snapshot.Revision >= targetRevision)
				{
					WorkspaceSaveStatus status = snapshot.FailedInstanceIds is { Count: > 0 }
						? WorkspaceSaveStatus.PartiallySaved
						: WorkspaceSaveStatus.Saved;
					if(requested > snapshot.Revision)
					{
						lock(_sync)
						{
							_scheduledSave = RunDebouncedSaveAsync(_lifetime.Token);
						}
					}
					return new WorkspaceSaveResult(status, snapshot.Revision, snapshot.FailedInstanceIds);
				}
			}
		}
		finally
		{
			_saveGate.Release();
		}
	}

	public async ValueTask DisposeAsync()
	{
		lock(_sync)
		{
			if(_deleted)
			{
				return;
			}
			_deleted = true;
			_lifetime.Cancel();
		}
		await _saveGate.WaitAsync();
		try
		{
			await _lease.DisposeAsync();
			_lifetime.Dispose();
		}
		finally
		{
			_saveGate.Release();
			_saveGate.Dispose();
		}
	}
}
