using System.Collections.Concurrent;

namespace Cockpit.Features.Workspace;

/// <summary>
/// Owns canonical workspace state by stable runtime-session key. It serialises reducer commits per
/// session while allowing unrelated sessions to update independently.
/// </summary>
sealed class WorkspaceStateFeature
{
	readonly ConcurrentDictionary<string, Entry> _states = new(StringComparer.Ordinal);

	internal event Action<string, WorkspaceState>? StateChanged;

	internal WorkspaceState GetOrCreate(string runtimeSessionKey, string sessionStorageId, Func<string> createRootGroupId)
	{
		Entry entry = _states.GetOrAdd(runtimeSessionKey, key => new Entry(WorkspaceState.Empty(key, sessionStorageId, createRootGroupId())));
		lock(entry.SyncRoot)
		{
			return entry.State;
		}
	}

	internal bool TryGet(string runtimeSessionKey, out WorkspaceState? state)
	{
		if(!_states.TryGetValue(runtimeSessionKey, out Entry? entry))
		{
			state = null;
			return false;
		}
		lock(entry.SyncRoot)
		{
			state = entry.State;
			return true;
		}
	}

	internal WorkspaceChangeResult Apply(
		string runtimeSessionKey,
		Func<WorkspaceState, WorkspaceChangeResult> reduce)
	{
		ArgumentNullException.ThrowIfNull(reduce);
		if(!_states.TryGetValue(runtimeSessionKey, out Entry? entry))
		{
			throw new KeyNotFoundException($"Workspace '{runtimeSessionKey}' is not attached.");
		}

		WorkspaceChangeResult result;
		lock(entry.SyncRoot)
		{
			result = reduce(entry.State);
			if(result.Status == WorkspaceChangeStatus.Applied)
			{
				entry.State = result.State;
			}
		}
		if(result.Status == WorkspaceChangeStatus.Applied)
		{
			StateChanged?.Invoke(runtimeSessionKey, result.State);
		}
		return result;
	}

	internal WorkspaceState Mutate(string runtimeSessionKey, Func<WorkspaceState, WorkspaceState> mutate)
	{
		ArgumentNullException.ThrowIfNull(mutate);
		if(!_states.TryGetValue(runtimeSessionKey, out Entry? entry))
		{
			throw new KeyNotFoundException($"Workspace '{runtimeSessionKey}' is not attached.");
		}

		WorkspaceState next;
		lock(entry.SyncRoot)
		{
			WorkspaceState previous = entry.State;
			next = mutate(previous);
			if(ReferenceEquals(previous, next))
			{
				return previous;
			}
			WorkspaceValidationResult validation = WorkspaceValidation.Validate(next);
			if(!validation.IsValid)
			{
				throw new InvalidOperationException(validation.Error);
			}
			entry.State = next;
		}
		StateChanged?.Invoke(runtimeSessionKey, next);
		return next;
	}

	internal void AttachRestored(WorkspaceState restored)
	{
		WorkspaceValidationResult validation = WorkspaceValidation.Validate(restored);
		if(!validation.IsValid)
		{
			throw new ArgumentException(validation.Error, nameof(restored));
		}
		_states[restored.RuntimeSessionKey] = new Entry(restored);
		StateChanged?.Invoke(restored.RuntimeSessionKey, restored);
	}

	internal bool Release(string runtimeSessionKey, out WorkspaceState? released)
	{
		if(!_states.TryRemove(runtimeSessionKey, out Entry? entry))
		{
			released = null;
			return false;
		}
		lock(entry.SyncRoot)
		{
			released = entry.State;
			return true;
		}
	}

	sealed class Entry(WorkspaceState state)
	{
		internal object SyncRoot { get; } = new();
		internal WorkspaceState State { get; set; } = state;
	}
}
