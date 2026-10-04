using System.Text;
using System.Text.Json;
using Cockpit.Tooling;
using Cockpit.Tools.GitChanges.Sources;
using Cockpit.Tools.GitChanges.State;

namespace Cockpit.Tools.GitChanges;

public sealed class GitChangesToolInstance : IWorkspaceToolInstance
{
	readonly IGitChangesToolSource _source;
	readonly SemaphoreSlim _refreshGate = new(1, 1);
	IDisposable? _subscription;
	long _acceptedGeneration = -1;
	long _mutationVersion;
	bool _disposed;

	public GitChangesToolInstance(WorkspaceToolInstanceContext context, IGitChangesToolSource source)
	{
		InstanceId = context.InstanceId;
		Session = context.Session;
		_source = source;
		State = new GitChangesState();
		_subscription = source.Subscribe(Session.RuntimeSessionKey.Value, Session.IdentityEpoch, OnSourceChanged);
	}

	public WorkspaceToolInstanceId InstanceId { get; }
	public WorkspaceSessionInfo Session { get; }
	public GitChangesState State { get; private set; }
	public GitRepositorySnapshot? Snapshot { get; private set; }
	public GitChangedFileSnapshot? DisplayedFile { get; private set; }
	public bool IsShowingCachedContent { get; private set; }
	public bool HasRepositoryMismatch { get; private set; }
	public string? RecoveryMessage { get; private set; }
	public event Action<ToolStateChanged>? StateChanged;
	public event Action? Changed;

	public async ValueTask RefreshAsync(CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		await _refreshGate.WaitAsync(cancellationToken);
		try
		{
			GitRepositorySnapshot? snapshot = await _source.GetSnapshotAsync(Session.RuntimeSessionKey.Value,
				Session.IdentityEpoch, State.RepositoryBinding, cancellationToken);
			if(snapshot is null || snapshot.RuntimeSessionKey != Session.RuntimeSessionKey.Value ||
				snapshot.IdentityEpoch != Session.IdentityEpoch || snapshot.Generation < _acceptedGeneration)
			{
				return;
			}

			_acceptedGeneration = snapshot.Generation;
			Snapshot = snapshot;
			HasRepositoryMismatch = State.RepositoryIdentity is not null &&
				!SameRepository(State.RepositoryIdentity, snapshot.Repository);
			if(HasRepositoryMismatch)
			{
				RecoveryMessage = "The saved content belongs to a different repository.";
				IsShowingCachedContent = DisplayedFile is not null;
			}
			else
			{
				ReconcileSelection(snapshot);
			}
			Changed?.Invoke();
		}
		catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested) { throw; }
		catch(Exception ex)
		{
			RecoveryMessage = ex.Message;
			IsShowingCachedContent = DisplayedFile is not null;
			Changed?.Invoke();
		}
		finally { _refreshGate.Release(); }
	}

	public async ValueTask<ToolRestoreResult> RestoreAsync(ToolStateReadContext? savedState, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if(savedState is null)
		{
			State = new GitChangesState();
			_mutationVersion = 0;
			return ToolRestoreResult.Restored();
		}
		if(savedState.SchemaVersion != GitChangesState.CurrentSchemaVersion)
		{
			return new ToolRestoreResult(ToolRestoreStatus.UnsupportedVersion);
		}

		try
		{
			GitChangesState? state = savedState.Metadata.Deserialize(GitChangesJsonContext.Default.GitChangesState);
			if(state is null)
			{
				return new ToolRestoreResult(ToolRestoreStatus.Invalid, ["State was empty."]);
			}

			if(state.CachedDisplay?.DiffAssetId is string assetId)
			{
				ToolAssetReference? asset = savedState.Assets.FirstOrDefault(a => a.Id == assetId);
				if(asset is not null)
				{
					await using Stream stream = await savedState.OpenAssetAsync(asset, cancellationToken);
					using StreamReader reader = new(stream, Encoding.UTF8, true, leaveOpen: false);
					string diff = await reader.ReadToEndAsync(cancellationToken);
					state = state with
					{
						CachedDisplay = state.CachedDisplay with
						{
							DisplayedFile = state.CachedDisplay.DisplayedFile is null ? null :
							state.CachedDisplay.DisplayedFile with { Diff = diff }
						}
					};
				}
			}
			State = state;
			_mutationVersion = savedState.MutationVersion;
			DisplayedFile = state.CachedDisplay?.DisplayedFile;
			IsShowingCachedContent = DisplayedFile is not null;
			return new ToolRestoreResult(IsShowingCachedContent ? ToolRestoreStatus.PartiallyRestored : ToolRestoreStatus.Restored);
		}
		catch(Exception ex) when(ex is JsonException or IOException)
		{
			return new ToolRestoreResult(ToolRestoreStatus.Invalid, [ex.Message]);
		}
	}

	public void SelectFile(GitChangedFileSnapshot? file)
	{
		DisplayedFile = file;
		UpdateState(State with
		{
			SelectedFile = file is null ? null : new GitFileSelection(file.RelativePath, file.PreviousRelativePath, file.Status),
			SelectedHunk = null,
			ScrollAnchor = null
		});
	}

	public void SetTreeView(bool value) => UpdateState(State with { TreeView = value });
	public void SetSplitView(bool value) => UpdateState(State with { SplitDiffView = value });
	public void SetFilter(string? value) => UpdateState(State with { FilterText = string.IsNullOrWhiteSpace(value) ? null : value.Trim() });
	public bool IsDirectoryExpanded(string key) => !State.DirectoryExpansionInitialized || State.ExpandedDirectoryKeys.Contains(key);
	public void ToggleDirectory(string key)
	{
		var expanded = State.DirectoryExpansionInitialized ? State.ExpandedDirectoryKeys : GetDirectoryKeys();
		UpdateState(State with
		{
			DirectoryExpansionInitialized = true,
			ExpandedDirectoryKeys = expanded.Contains(key) ? expanded.Remove(key) : expanded.Add(key)
		});
	}

	public async ValueTask<bool> RevealSelectedAsync(CancellationToken cancellationToken)
	{
		if(DisplayedFile is null || Snapshot is null || DisplayedFile.Status == GitChangeStatus.Deleted)
		{
			return false;
		}

		return await _source.RevealFileAsync(Session.RuntimeSessionKey.Value, Session.IdentityEpoch,
			Snapshot.Repository.CanonicalRoot, DisplayedFile.RelativePath, cancellationToken);
	}

	public async ValueTask<ToolCheckpoint> CaptureAsync(ToolStateWriteContext destination, CancellationToken cancellationToken)
	{
		CachedGitDisplay? display = Snapshot is null ? State.CachedDisplay : new CachedGitDisplay
		{
			Repository = Snapshot.Repository,
			Listing = [.. Snapshot.Files.Select(static f => f with { Diff = null })],
			DisplayedFile = DisplayedFile,
			CapturedAt = DateTimeOffset.UtcNow
		};
		List<ToolAssetReference> assets = [];
		if(display?.DisplayedFile?.Diff is string diff)
		{
			await using ToolAssetWriter writer = await destination.CreateAssetAsync("selected.diff", "text/x-diff", cancellationToken);
			await writer.Stream.WriteAsync(Encoding.UTF8.GetBytes(diff), cancellationToken);
			ToolAssetReference asset = await writer.CommitAsync(cancellationToken);
			assets.Add(asset);
			display = display with
			{
				DiffAssetId = asset.Id,
				DisplayedFile = display.DisplayedFile with { Diff = null }
			};
		}
		GitChangesState captured = State with { RepositoryIdentity = Snapshot?.Repository ?? State.RepositoryIdentity, CachedDisplay = display };
		return new ToolCheckpoint(GitChangesState.CurrentSchemaVersion, Volatile.Read(ref _mutationVersion),
			JsonSerializer.SerializeToElement(captured, GitChangesJsonContext.Default.GitChangesState), assets,
			display is null ? ToolRestorationFidelity.ConfigurationOnly : ToolRestorationFidelity.Exact);
	}

	public ValueTask OnActivatedAsync(WorkspaceToolActivationContext context, CancellationToken cancellationToken) =>
		new(RefreshAsync(cancellationToken).AsTask());
	public ValueTask OnDeactivatedAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
	public ValueTask OnLayoutChangedAsync(WorkspaceToolLayoutContext context, CancellationToken cancellationToken) => ValueTask.CompletedTask;
	public ValueTask<ToolCloseDecision> CanCloseAsync(ToolCloseReason reason, CancellationToken cancellationToken) => ValueTask.FromResult(ToolCloseDecision.Allow());

	void ReconcileSelection(GitRepositorySnapshot snapshot)
	{
		GitFileSelection? selected = State.SelectedFile;
		if(selected is null)
		{
			DisplayedFile = snapshot.Files.FirstOrDefault();
			RecoveryMessage = null;
			IsShowingCachedContent = false;
			return;
		}
		GitChangedFileSnapshot? current = snapshot.Files.FirstOrDefault(file =>
			string.Equals(file.RelativePath, selected.RelativePath, StringComparison.Ordinal) ||
			(selected.PreviousRelativePath is not null && string.Equals(file.PreviousRelativePath, selected.PreviousRelativePath, StringComparison.Ordinal)));
		if(current is null)
		{
			RecoveryMessage = "The saved file is no longer in the current change set.";
			IsShowingCachedContent = DisplayedFile is not null;
			return;
		}
		DisplayedFile = current;
		RecoveryMessage = null;
		IsShowingCachedContent = false;
	}

	void UpdateState(GitChangesState state)
	{
		if(state == State)
		{
			return;
		}

		State = state;
		long version = Interlocked.Increment(ref _mutationVersion);
		StateChanged?.Invoke(new ToolStateChanged(InstanceId, version));
		Changed?.Invoke();
	}

	void OnSourceChanged(GitSourceChange change)
	{
		if(change.RuntimeSessionKey != Session.RuntimeSessionKey.Value || change.IdentityEpoch != Session.IdentityEpoch ||
			change.RepositoryGeneration <= _acceptedGeneration)
		{
			return;
		}

		_ = RefreshAsync().AsTask();
	}

	static bool SameRepository(GitRepositoryIdentity left, GitRepositoryIdentity right) =>
		string.Equals(Path.GetFullPath(left.CanonicalRoot), Path.GetFullPath(right.CanonicalRoot),
			OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) &&
		string.Equals(left.Origin, right.Origin, StringComparison.Ordinal);

	System.Collections.Immutable.ImmutableHashSet<string> GetDirectoryKeys()
	{
		var builder = System.Collections.Immutable.ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
		foreach(GitChangedFileSnapshot file in Snapshot?.Files ?? State.CachedDisplay?.Listing ?? [])
		{
			string[] parts = file.RelativePath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
			string key = string.Empty;
			for(int index = 0; index < parts.Length - 1; index++)
			{
				key = string.IsNullOrEmpty(key) ? parts[index] : $"{key}/{parts[index]}";
				builder.Add(key);
			}
		}
		return builder.ToImmutable();
	}

	public ValueTask DisposeAsync()
	{
		if(_disposed)
		{
			return ValueTask.CompletedTask;
		}

		_disposed = true;
		_subscription?.Dispose();
		_subscription = null;
		_refreshGate.Dispose();
		return ValueTask.CompletedTask;
	}
}
