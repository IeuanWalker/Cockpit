namespace Cockpit.Features.Sessions.Models;

internal readonly record struct SdkLifecycleTransition(long Version);

/// <summary>
/// State that describes the SDK connection and agent lifecycle for a session.
/// Pending interactions and UI state must not overwrite these values.
/// </summary>
public sealed class SessionLifecycleState
{
	readonly Lock _syncRoot = new();
	AgentRunStateEnum _agentRunState = AgentRunStateEnum.Idle;
	SdkSessionStateEnum _sdkState = SdkSessionStateEnum.NotLoaded;
	long _sdkStateVersion;
	readonly PendingChange _modelChange = new();
	readonly PendingChange _agentChange = new();
	readonly PendingChange _agentModeChange = new();
	bool _suppressFinishedNotification;

	public AgentRunStateEnum AgentRunState
	{
		get { lock(_syncRoot) { return _agentRunState; } }
		internal set => SetAgentRunState(value);
	}

	public SdkSessionStateEnum SdkState
	{
		get { lock(_syncRoot) { return _sdkState; } }
		internal set => SetSdkState(value);
	}

	public bool ModelChanged
	{
		get => IsPending(_modelChange);
		internal set => SetPending(_modelChange, value);
	}

	public bool AgentChanged
	{
		get => IsPending(_agentChange);
		internal set => SetPending(_agentChange, value);
	}

	public bool AgentModeChanged
	{
		get => IsPending(_agentModeChange);
		internal set => SetPending(_agentModeChange, value);
	}

	/// <summary>
	/// Prevents history replay from raising a session-finished notification.
	/// </summary>
	public bool SuppressFinishedNotification
	{
		get { lock(_syncRoot) { return _suppressFinishedNotification; } }
		internal set { lock(_syncRoot) { _suppressFinishedNotification = value; } }
	}

	internal void SetAgentRunState(AgentRunStateEnum state)
	{
		lock(_syncRoot)
		{
			_agentRunState = state;
		}
	}

	internal void SetSdkState(SdkSessionStateEnum state)
	{
		lock(_syncRoot)
		{
			_sdkState = state;
			_sdkStateVersion++;
		}
	}

	internal bool TryTransitionSdkState(SdkSessionStateEnum expected, SdkSessionStateEnum next)
	{
		lock(_syncRoot)
		{
			if(_sdkState != expected)
			{
				return false;
			}

			_sdkState = next;
			_sdkStateVersion++;
			return true;
		}
	}

	internal bool TryBeginSdkTransition(
		SdkSessionStateEnum expected,
		SdkSessionStateEnum inProgress,
		out SdkLifecycleTransition transition)
	{
		lock(_syncRoot)
		{
			if(_sdkState != expected)
			{
				transition = default;
				return false;
			}

			_sdkState = inProgress;
			transition = new(++_sdkStateVersion);
			return true;
		}
	}

	internal bool TryCompleteSdkTransition(
		SdkLifecycleTransition transition,
		SdkSessionStateEnum expected,
		SdkSessionStateEnum next)
	{
		lock(_syncRoot)
		{
			if(_sdkStateVersion != transition.Version || _sdkState != expected)
			{
				return false;
			}

			_sdkState = next;
			_sdkStateVersion++;
			return true;
		}
	}

	internal bool TryCompleteLoad(SdkLifecycleTransition transition)
	{
		lock(_syncRoot)
		{
			if(_sdkStateVersion != transition.Version || _sdkState != SdkSessionStateEnum.Loading)
			{
				return false;
			}

			_agentRunState = AgentRunStateEnum.Idle;
			_sdkState = SdkSessionStateEnum.Loaded;
			_sdkStateVersion++;
			return true;
		}
	}

	internal void MarkModelChanged() => MarkPending(_modelChange);
	internal long? CaptureModelChange() => CapturePending(_modelChange);
	internal void ClearModelChanged() => ClearModelChanged(null);
	internal void ClearModelChanged(long? handledVersion) => ClearPending(_modelChange, handledVersion);

	internal void MarkAgentChanged() => MarkPending(_agentChange);
	internal long? CaptureAgentChange() => CapturePending(_agentChange);
	internal void ClearAgentChanged() => ClearAgentChanged(null);
	internal void ClearAgentChanged(long? handledVersion) => ClearPending(_agentChange, handledVersion);

	internal void MarkAgentModeChanged() => MarkPending(_agentModeChange);
	internal long? CaptureAgentModeChange() => CapturePending(_agentModeChange);
	internal void ClearAgentModeChanged() => ClearAgentModeChanged(null);
	internal void ClearAgentModeChanged(long? handledVersion) => ClearPending(_agentModeChange, handledVersion);

	internal void ResetForEviction()
	{
		lock(_syncRoot)
		{
			_sdkState = SdkSessionStateEnum.NotLoaded;
			_sdkStateVersion++;
			_modelChange.IsPending = false;
			_agentChange.IsPending = false;
			_agentModeChange.IsPending = false;
		}
	}

	internal void SetSuppressFinishedNotification(bool suppress)
	{
		lock(_syncRoot)
		{
			_suppressFinishedNotification = suppress;
		}
	}

	bool IsPending(PendingChange change)
	{
		lock(_syncRoot)
		{
			return change.IsPending;
		}
	}

	void SetPending(PendingChange change, bool isPending)
	{
		if(isPending)
		{
			MarkPending(change);
		}
		else
		{
			ClearPending(change, null);
		}
	}

	void MarkPending(PendingChange change)
	{
		lock(_syncRoot)
		{
			change.IsPending = true;
			change.Version++;
		}
	}

	long? CapturePending(PendingChange change)
	{
		lock(_syncRoot)
		{
			return change.IsPending ? change.Version : null;
		}
	}

	void ClearPending(PendingChange change, long? handledVersion)
	{
		lock(_syncRoot)
		{
			if(handledVersion is null || handledVersion == change.Version)
			{
				change.IsPending = false;
			}
		}
	}

	sealed class PendingChange
	{
		internal bool IsPending { get; set; }
		internal long Version { get; set; }
	}
}
