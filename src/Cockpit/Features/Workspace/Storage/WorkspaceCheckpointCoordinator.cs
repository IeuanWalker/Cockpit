namespace Cockpit.Features.Workspace.Storage;

enum WorkspaceCheckpointAcceptStatus
{
	Accepted,
	Stale,
	InstanceClosed,
	UnknownInstance
}

sealed record WorkspaceCheckpointStatus(
	string InstanceId,
	long ControllerGeneration,
	long RequestedVersion,
	long AcceptedVersion,
	WorkspaceCheckpointReference? Checkpoint);

/// <summary>
/// Rejects checkpoint completions from an old controller generation or a closed instance. Capture
/// work remains tool-owned and runs outside this lock; only immutable completion metadata enters it.
/// </summary>
sealed class WorkspaceCheckpointCoordinator
{
	readonly Lock _sync = new();
	readonly Dictionary<string, Slot> _slots = new(StringComparer.Ordinal);
	long _nextControllerGeneration;

	internal long BeginControllerGeneration(string instanceId, WorkspaceCheckpointReference? durableCheckpoint = null)
	{
		lock(_sync)
		{
			long generation = checked(++_nextControllerGeneration);
			if(_slots.TryGetValue(instanceId, out Slot? existing))
			{
				existing.ControllerGeneration = generation;
				existing.IsClosed = false;
				if(durableCheckpoint is not null && durableCheckpoint.StateVersion > existing.AcceptedVersion)
				{
					existing.AcceptedVersion = durableCheckpoint.StateVersion;
					existing.Checkpoint = durableCheckpoint;
				}
			}
			else
			{
				_slots[instanceId] = new Slot
				{
					ControllerGeneration = generation,
					RequestedVersion = durableCheckpoint?.StateVersion ?? -1,
					AcceptedVersion = durableCheckpoint?.StateVersion ?? -1,
					Checkpoint = durableCheckpoint
				};
			}
			return generation;
		}
	}

	internal bool RequestCapture(string instanceId, long controllerGeneration, long stateVersion)
	{
		lock(_sync)
		{
			if(!_slots.TryGetValue(instanceId, out Slot? slot)
				|| slot.IsClosed
				|| slot.ControllerGeneration != controllerGeneration)
			{
				return false;
			}
			slot.RequestedVersion = Math.Max(slot.RequestedVersion, stateVersion);
			return true;
		}
	}

	internal WorkspaceCheckpointAcceptStatus TryAccept(
		string instanceId,
		long controllerGeneration,
		WorkspaceCheckpointReference checkpoint)
	{
		lock(_sync)
		{
			if(!_slots.TryGetValue(instanceId, out Slot? slot))
			{
				return WorkspaceCheckpointAcceptStatus.UnknownInstance;
			}
			if(slot.IsClosed)
			{
				return WorkspaceCheckpointAcceptStatus.InstanceClosed;
			}
			if(slot.ControllerGeneration != controllerGeneration || checkpoint.StateVersion <= slot.AcceptedVersion)
			{
				return WorkspaceCheckpointAcceptStatus.Stale;
			}
			slot.AcceptedVersion = checkpoint.StateVersion;
			slot.Checkpoint = checkpoint;
			return WorkspaceCheckpointAcceptStatus.Accepted;
		}
	}

	internal void CloseInstance(string instanceId)
	{
		lock(_sync)
		{
			if(_slots.TryGetValue(instanceId, out Slot? slot))
			{
				slot.IsClosed = true;
				slot.ControllerGeneration = checked(++_nextControllerGeneration);
			}
		}
	}

	internal IReadOnlyList<WorkspaceCheckpointStatus> Snapshot()
	{
		lock(_sync)
		{
			return _slots
				.Where(pair => !pair.Value.IsClosed)
				.Select(pair => new WorkspaceCheckpointStatus(
					pair.Key,
					pair.Value.ControllerGeneration,
					pair.Value.RequestedVersion,
					pair.Value.AcceptedVersion,
					pair.Value.Checkpoint))
				.ToArray();
		}
	}

	sealed class Slot
	{
		internal long ControllerGeneration { get; set; }
		internal long RequestedVersion { get; set; }
		internal long AcceptedVersion { get; set; }
		internal WorkspaceCheckpointReference? Checkpoint { get; set; }
		internal bool IsClosed { get; set; }
	}
}
