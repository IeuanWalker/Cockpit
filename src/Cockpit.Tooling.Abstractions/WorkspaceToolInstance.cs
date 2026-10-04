using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Cockpit.Tooling;

/// <summary>A typed base for tools whose durable state fits in immutable JSON metadata.</summary>
public abstract class WorkspaceToolInstance<TState> : IWorkspaceToolInstance
{
	readonly JsonTypeInfo<TState> _jsonTypeInfo;
	long _mutationVersion;
	TState? _state;

	protected WorkspaceToolInstance(
		WorkspaceToolInstanceId instanceId,
		JsonTypeInfo<TState> jsonTypeInfo)
	{
		InstanceId = instanceId;
		_jsonTypeInfo = jsonTypeInfo ?? throw new ArgumentNullException(nameof(jsonTypeInfo));
	}

	public WorkspaceToolInstanceId InstanceId { get; }
	public event Action<ToolStateChanged>? StateChanged;

	protected TState State => _state ?? throw new InvalidOperationException("RestoreAsync must complete before state is accessed.");
	protected long MutationVersion => Volatile.Read(ref _mutationVersion);

	protected abstract int SchemaVersion { get; }
	protected abstract TState CreateDefaultState();

	protected virtual ValueTask<(ToolRestoreStatus Status, TState? State)> MigrateAsync(
		ToolStateReadContext source,
		CancellationToken cancellationToken) =>
		ValueTask.FromResult<(ToolRestoreStatus, TState?)>((ToolRestoreStatus.UnsupportedVersion, default));

	protected void SetState(TState state, WorkspaceViewGeneration? generation = null)
	{
		ArgumentNullException.ThrowIfNull(state);
		_state = state;
		long version = Interlocked.Increment(ref _mutationVersion);
		StateChanged?.Invoke(new ToolStateChanged(InstanceId, version, generation));
	}

	public async ValueTask<ToolRestoreResult> RestoreAsync(
		ToolStateReadContext? savedState,
		CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if(savedState is null)
		{
			_state = CreateDefaultState();
			_mutationVersion = 0;
			return ToolRestoreResult.Restored();
		}

		if(savedState.SchemaVersion != SchemaVersion)
		{
			(ToolRestoreStatus Status, TState? State) migrated = await MigrateAsync(savedState, cancellationToken).ConfigureAwait(false);
			if(migrated.State is null)
			{
				return new ToolRestoreResult(migrated.Status);
			}

			_state = migrated.State;
			_mutationVersion = savedState.MutationVersion;
			return new ToolRestoreResult(migrated.Status);
		}

		try
		{
			_state = savedState.Metadata.Deserialize(_jsonTypeInfo)
				?? throw new JsonException("Tool state deserialized to null.");
			_mutationVersion = savedState.MutationVersion;
			return ToolRestoreResult.Restored();
		}
		catch(JsonException exception)
		{
			return new ToolRestoreResult(ToolRestoreStatus.Invalid, [exception.Message]);
		}
	}

	public virtual ValueTask<ToolCheckpoint> CaptureAsync(
		ToolStateWriteContext destination,
		CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		JsonElement metadata = JsonSerializer.SerializeToElement(State, _jsonTypeInfo);
		return ValueTask.FromResult(new ToolCheckpoint(
			SchemaVersion,
			MutationVersion,
			metadata,
			[]));
	}

	public virtual ValueTask OnActivatedAsync(WorkspaceToolActivationContext context, CancellationToken cancellationToken) =>
		ValueTask.CompletedTask;

	public virtual ValueTask OnDeactivatedAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

	public virtual ValueTask OnLayoutChangedAsync(WorkspaceToolLayoutContext context, CancellationToken cancellationToken) =>
		ValueTask.CompletedTask;

	public virtual ValueTask<ToolCloseDecision> CanCloseAsync(ToolCloseReason reason, CancellationToken cancellationToken) =>
		ValueTask.FromResult(ToolCloseDecision.Allow());

	public virtual ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
