using Cockpit.Features.Sessions.Models;
using GitHub.Copilot;
using Microsoft.Extensions.Logging;

namespace Cockpit.Features.Sessions;

public sealed partial class SessionFeature
{
	const int idleEvictionMinutes = 30;
	const int evictionCheckIntervalMinutes = 5;

	void StartEvictionLoop()
	{
		_ = RunEvictionLoopAsync(_evictionCts.Token);
	}

	async Task RunEvictionLoopAsync(CancellationToken ct)
	{
		using PeriodicTimer timer = new(TimeSpan.FromMinutes(evictionCheckIntervalMinutes));
		try
		{
			while(await timer.WaitForNextTickAsync(ct))
			{
				try
				{
					await EvictIdleSessionsAsync(ct);
				}
				catch(Exception ex)
				{
					_logger.LogError(ex, "Idle session eviction encountered an error");
				}
			}
		}
		catch(OperationCanceledException)
		{
			// Normal shutdown — swallow
		}
	}

	async Task EvictIdleSessionsAsync(CancellationToken ct)
	{
		List<SessionModel> candidates = [.. _sessionListFeature.Sessions.Where(IsEvictionCandidate)];

		int evictedCount = 0;
		foreach(SessionModel session in candidates)
		{
			_logger.LogInformation(
				"Evicting idle session {SessionId} (last activity: {LastActivity})",
				session.Id,
				session.LastActivity);

			bool evicted = await UnloadSessionAsync(session, ct);
			if(evicted)
			{
				evictedCount++;
			}
		}

		if(evictedCount > 0)
		{
			_logger.LogInformation("Evicted {Count} idle session(s) to free memory", evictedCount);
			_sessionListFeature.NotifyStateChanged();
		}
	}

	bool IsEvictionCandidate(SessionModel session)
	{
		if(session == _sessionListFeature.CurrentSession)
		{
			return false;
		}

		if(session.SdkState is not (SdkSessionStateEnum.Loaded or SdkSessionStateEnum.Resumed))
		{
			return false;
		}

		if(session.Status is not SessionStatusEnum.Idle)
		{
			return false;
		}

		if(session.IsCompacting)
		{
			return false;
		}

		return (DateTime.UtcNow - session.LastActivity).TotalMinutes > idleEvictionMinutes;
	}

	const int disposeTimeoutSeconds = 10;

	/// <summary>
	/// Disposes the live SDK session and clears all in-memory data back to the minimal state
	/// set by <c>RefreshExistingSessions</c> (metadata only — no messages, no context panel data).
	/// The session can be fully re-loaded on next access via <see cref="LoadSession"/>.
	/// All eligibility checks are re-validated inside <see cref="SessionModel.SessionEventLock"/>
	/// to guard against races with <see cref="LoadSession"/>.
	/// </summary>
	async Task<bool> UnloadSessionAsync(SessionModel session, CancellationToken ct)
	{
		CopilotSession? sdkSession;
		lock(session.SessionEventLock)
		{
			// Re-check inside the lock: the session may have become current or changed
			// state between candidate selection and now (e.g. user clicked it, compaction started).
			if(!IsEvictionCandidate(session))
			{
				return false;
			}

			// Transition to NotLoaded first so LoadSession performs a full reload instead of
			// fast-pathing to a now-empty session if called concurrently.
			session.SdkState = SdkSessionStateEnum.NotLoaded;

			// Clear message history
			session.Messages = []; // setter also clears MessagesSnapshot
			session.ActiveWorkingGroup = null;
			session.StreamingMessages.Clear();
			session.StreamingThinkingEvents.Clear();
			session.TokenUsageInfo = null;
			session.PendingMessageCount = 0;
			session.PendingTaskSummary = null;

			// Clear pending model/agent change flags so reload doesn't make redundant SDK calls
			session.ModelChanged = false;
			session.AgentChanged = false;
			session.AgentModeChanged = false;

			// Clear context panel data (populated by LoadContextPanelDataAsync)
			session.Context.Agents = [];
			session.Context.Instructions = [];
			session.Context.McpServers = [];
			session.Context.Skills = [];
			session.Context.Plugins = [];

			// Clear data populated on session switch/resume
			session.Context.EditedFiles = [];
			session.Context.AllowedCommands = [];

			// Remove from registry inside the lock to prevent HandleSessionEvent from
			// processing any further events on this session's cleared state.
			_sdkRegistry.TryRemove(session.Id, out sdkSession);
		}

		// Clear fields protected by their own locks — outside SessionEventLock to avoid
		// potential lock-ordering deadlocks with other code paths.
		lock(session.Context.SessionPermissionCommandsLock)
		{
			session.Context.SessionPermissionCommands = [];
		}

		lock(session.StatusHistoryLock)
		{
			session.StatusHistory.Clear();
		}

		session.PendingPermissionRequests.Clear();
		session.PendingUserInputRequests.Clear();

		// Cancel any awaiting TCS-based handlers so SDK threads don't hang indefinitely.
		_permissionHandler.CancelPendingRequestsForSession(session.Id);
		_userInputHandler.CancelPendingRequestsForSession(session.Id);

		if(sdkSession is not null)
		{
			try
			{
				await sdkSession.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(disposeTimeoutSeconds), ct);
			}
			catch(TimeoutException)
			{
				_logger.LogWarning("Timed out disposing SDK session {SessionId}; continuing eviction", session.Id);
			}
			catch(OperationCanceledException)
			{
				// Shutdown requested — abandon waiting for dispose
			}
		}

		return true;
	}
}
