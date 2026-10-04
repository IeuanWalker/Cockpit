using System.Collections.Immutable;
using System.Text.Json;
using Cockpit.Tooling;
using Cockpit.Tools.SessionContext.Sources;
using Cockpit.Tools.SessionContext.State;

namespace Cockpit.Tools.SessionContext;

/// <summary>Owns presentation state for one context tab; source data may be shared.</summary>
public sealed class SessionContextToolInstance : IWorkspaceToolInstance
{
	readonly ISessionContextToolSource _source;
	readonly SemaphoreSlim _refreshGate = new(1, 1);
	IDisposable? _subscription;
	long _acceptedGeneration = -1;
	bool _disposed;

	readonly IWorkspaceToolHost _host;
	long _mutationVersion;

	public SessionContextToolInstance(WorkspaceToolInstanceContext context, ISessionContextToolSource source)
	{
		InstanceId = context.InstanceId;
		RuntimeSessionKey = context.Session.RuntimeSessionKey;
		IdentityEpoch = context.Session.IdentityEpoch;
		_host = context.Host;
		_source = source;
		State = new SessionContextState();
		_subscription = source.Subscribe(RuntimeSessionKey.Value, IdentityEpoch, OnSourceChanged);
	}

	public WorkspaceToolInstanceId InstanceId { get; }
	public RuntimeSessionKey RuntimeSessionKey { get; }
	public long IdentityEpoch { get; }
	public SessionContextState State { get; private set; }
	public SessionContextSnapshot? Snapshot { get; private set; }
	public bool IsShowingCachedSnapshot { get; private set; }
	public string? Error { get; private set; }
	public event Action? Changed;
	public event Action<ToolStateChanged>? StateChanged;

	public async ValueTask RefreshAsync(CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		await _refreshGate.WaitAsync(cancellationToken);
		try
		{
			SessionContextSnapshot? snapshot = await _source.GetSnapshotAsync(
				RuntimeSessionKey.Value, IdentityEpoch, cancellationToken);
			if(snapshot is null || snapshot.RuntimeSessionKey != RuntimeSessionKey.Value ||
				snapshot.IdentityEpoch != IdentityEpoch || snapshot.Generation < _acceptedGeneration)
			{
				return;
			}

			_acceptedGeneration = snapshot.Generation;
			Snapshot = snapshot;
			IsShowingCachedSnapshot = false;
			Error = null;
			Changed?.Invoke();
		}
		catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch(Exception ex)
		{
			Error = ex.Message;
			IsShowingCachedSnapshot = Snapshot is not null;
			Changed?.Invoke();
		}
		finally
		{
			_refreshGate.Release();
		}
	}

	public ValueTask<ToolRestoreResult> RestoreAsync(ToolStateReadContext? savedState, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if(savedState is null)
		{
			RestoreState(new SessionContextState(), 0);
			return ValueTask.FromResult(ToolRestoreResult.Restored());
		}

		if(savedState.SchemaVersion is not (1 or SessionContextState.CurrentSchemaVersion))
		{
			return ValueTask.FromResult(new ToolRestoreResult(ToolRestoreStatus.UnsupportedVersion));
		}

		try
		{
			SessionContextState? state = savedState.Metadata.Deserialize(SessionContextJsonContext.Default.SessionContextState);
			if(state is null)
			{
				return ValueTask.FromResult(new ToolRestoreResult(ToolRestoreStatus.Invalid, ["State was empty."]));
			}
			// Version 1 cached the pre-tool-panel, loosely typed snapshot. Keep the
			// user's presentation choices, but never display that obsolete payload.
			if(savedState.SchemaVersion == 1)
			{
				state = state with
				{
					SchemaVersion = SessionContextState.CurrentSchemaVersion,
					CachedContext = null
				};
			}
			RestoreState(state, savedState.MutationVersion);
			return ValueTask.FromResult(ToolRestoreResult.Restored());
		}
		catch(JsonException ex)
		{
			return ValueTask.FromResult(new ToolRestoreResult(ToolRestoreStatus.Invalid, [ex.Message]));
		}
	}

	void RestoreState(SessionContextState state, long mutationVersion)
	{
		State = state;
		Snapshot = state.CachedContext?.Snapshot;
		IsShowingCachedSnapshot = Snapshot is not null;
		// Generation is scoped to the live source process. A saved snapshot may
		// have a larger value than the newly-created source after an app restart,
		// so it must not prevent the first authoritative refresh from winning.
		_acceptedGeneration = -1;
		_mutationVersion = mutationVersion;
		Changed?.Invoke();
	}

	public void SetFilter(string? filter) => UpdateState(State with { FilterText = NormalizeFilter(filter) });

	public void SetSectionVisible(string sectionId, bool visible) =>
		UpdateState(State with { SectionVisibility = State.SectionVisibility.SetItem(sectionId, visible) });

	public void SetSectionExpanded(string sectionId, bool expanded) =>
		UpdateState(State with { SectionExpansion = State.SectionExpansion.SetItem(sectionId, expanded) });

	public void SetSectionOrder(IEnumerable<string> sectionIds)
	{
		ImmutableArray<string> order = [.. sectionIds.Where(static id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal)];
		UpdateState(State with { SectionOrder = order });
	}

	public void Select(string? sectionId, string? itemKey)
	{
		SessionContextSelection? selection = sectionId is not null && itemKey is not null
			? new(sectionId, itemKey)
			: null;
		UpdateState(State with { SelectedItem = selection });
	}

	public void SetScrollAnchor(SessionContextScrollAnchor? anchor) =>
		UpdateState(State with { ScrollAnchor = anchor });

	public SessionContextState CaptureState(bool includeCachedContent)
	{
		CachedSessionContext? cached = null;
		if(includeCachedContent && Snapshot is not null)
		{
			cached = new CachedSessionContext
			{
				Snapshot = Snapshot,
				SourceIdentity = $"{RuntimeSessionKey.Value}:{IdentityEpoch}",
				CachedAt = DateTimeOffset.UtcNow
			};
		}

		return State with { CachedContext = cached };
	}

	public SessionContextState ExportTemplate(bool includeCachedReference = false) =>
		CaptureState(includeCachedReference) with
		{
			SelectedItem = State.SelectedItem,
			ScrollAnchor = null
		};

	void UpdateState(SessionContextState state)
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

	void OnSourceChanged(SessionContextSourceChange change)
	{
		if(change.RuntimeSessionKey != RuntimeSessionKey.Value || change.IdentityEpoch != IdentityEpoch ||
			change.Generation <= _acceptedGeneration)
		{
			return;
		}

		_ = RefreshAsync().AsTask();
	}

	static string? NormalizeFilter(string? filter) =>
		string.IsNullOrWhiteSpace(filter) ? null : filter.Trim();

	public ValueTask<ToolCheckpoint> CaptureAsync(ToolStateWriteContext destination, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		SessionContextState captured = CaptureState(includeCachedContent: true);
		return ValueTask.FromResult(new ToolCheckpoint(
			SessionContextState.CurrentSchemaVersion,
			Volatile.Read(ref _mutationVersion),
			JsonSerializer.SerializeToElement(captured, SessionContextJsonContext.Default.SessionContextState),
			[]));
	}

	public ValueTask OnActivatedAsync(WorkspaceToolActivationContext context, CancellationToken cancellationToken) =>
		new(RefreshAsync(cancellationToken).AsTask());

	public ValueTask OnDeactivatedAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

	public ValueTask OnLayoutChangedAsync(WorkspaceToolLayoutContext context, CancellationToken cancellationToken) =>
		ValueTask.CompletedTask;

	public ValueTask<ToolCloseDecision> CanCloseAsync(ToolCloseReason reason, CancellationToken cancellationToken) =>
		ValueTask.FromResult(ToolCloseDecision.Allow());

	public ValueTask<WorkspaceHostCommandResult> AddSelectedToMessageAsync(CancellationToken cancellationToken)
	{
		SessionContextSelection? selection = State.SelectedItem;
		if(selection is null)
		{
			return ValueTask.FromResult(new WorkspaceHostCommandResult(WorkspaceHostCommandStatus.InvalidTarget));
		}
		return AddAsync(selection, cancellationToken);
	}

	public async ValueTask<SessionContextCommandResult> ExecuteAsync(
		SessionContextCommand command,
		string? sectionId = null,
		string? itemKey = null,
		string? argument = null,
		CancellationToken cancellationToken = default)
	{
		SessionContextCommandResult result = await _source.ExecuteAsync(
			RuntimeSessionKey.Value,
			IdentityEpoch,
			new(command, sectionId, itemKey, argument),
			cancellationToken);
		if(result.Applied)
		{
			await RefreshAsync(cancellationToken);
		}
		else if(!string.IsNullOrWhiteSpace(result.Error))
		{
			Error = result.Error;
			Changed?.Invoke();
		}
		return result;
	}

	async ValueTask<WorkspaceHostCommandResult> AddAsync(SessionContextSelection selection, CancellationToken cancellationToken)
	{
		bool accepted = await _source.AddItemToMessageAsync(RuntimeSessionKey.Value, IdentityEpoch,
			selection.SectionId, selection.ItemKey, cancellationToken);
		return accepted ? WorkspaceHostCommandResult.Applied() :
			new WorkspaceHostCommandResult(WorkspaceHostCommandStatus.SessionNotCurrent);
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
