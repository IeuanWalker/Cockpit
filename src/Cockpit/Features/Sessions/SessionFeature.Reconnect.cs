using Cockpit.Features.MessageMode;
using Cockpit.Features.Sdk;
using Cockpit.Features.Sessions.Models;
using Cockpit.Features.SessionEvents;
using Cockpit.Features.SessionEvents.Handlers;
using Cockpit.Features.SessionEvents.Models;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging;
using System.Threading.Channels;

namespace Cockpit.Features.Sessions;

public sealed partial class SessionFeature
{
	readonly Channel<ConnectionState> _reconnectChannel = Channel.CreateBounded<ConnectionState>(
		new BoundedChannelOptions(4)
		{
			FullMode = BoundedChannelFullMode.DropOldest,
			SingleReader = true
		});

	/// <summary>
	/// Routes <see cref="CopilotClientFeature.OnConnectionStateChanged"/> events into the
	/// serialized <see cref="_reconnectChannel"/> so that disconnect and reconnect handlers
	/// are never executed concurrently.
	/// </summary>
	void HandleConnectionStateChanged(ConnectionState state)
	{
		_reconnectChannel.Writer.TryWrite(state);
	}

	/// <summary>
	/// Starts the background loop that drains <see cref="_reconnectChannel"/> and calls
	/// the appropriate handler sequentially, eliminating the disconnect/reconnect race.
	/// </summary>
	void StartReconnectLoop()
	{
		_ = Task.Run(() => ProcessConnectionEventsAsync(_evictionCts.Token));
	}

	async Task ProcessConnectionEventsAsync(CancellationToken cancellationToken)
	{
		try
		{
			await foreach(ConnectionState state in _reconnectChannel.Reader.ReadAllAsync(cancellationToken))
			{
				try
				{
					if(state == ConnectionState.Disconnected)
					{
						await HandleClientDisconnectedAsync();
					}
					else
					{
						await HandleClientReconnectedAsync();
					}
				}
				catch(Exception ex)
				{
					_logger.LogWarning(ex, "Unhandled exception processing connection state {State}", state);
				}
			}
		}
		catch(OperationCanceledException)
		{
			// Expected on shutdown.
		}
	}

	/// <summary>
	/// Called when the SDK client is invalidated (pipe broken, process died, etc.).
	/// Keeps any in-progress <see cref="ActivityGroupModel"/> alive and running so the
	/// working panel stays visible. Resets only the SDK bookkeeping that has become invalid:
	/// streaming state, registry entry, and blocking permission/input requests. Message
	/// history is preserved.
	/// </summary>
	async Task HandleClientDisconnectedAsync()
	{
		_logger.LogInformation("Client disconnected — resetting SDK state for active sessions");

		List<SessionModel> sessions = [.. _sessionListFeature.Sessions];

		foreach(SessionModel session in sessions)
		{
			CopilotSession? sdkSession = null;

			lock(session.SessionEventLock)
			{
				if(session.SdkState == SdkSessionStateEnum.NotLoaded)
				{
					continue;
				}

				// Clear streaming-only state (the pipe is dead; no more deltas will arrive).
				session.StreamingMessages.Clear();
				session.StreamingThinkingEvents.Clear();

				// Reset to NotLoaded so the reconnect handler (or user navigation) triggers a reload.
				session.SdkState = SdkSessionStateEnum.NotLoaded;

				// If the session was blocked waiting for permission/input, those requests are now
				// dead. Restore status so the working panel reflects the active group correctly.
				// Guard: if there's no active group the session should end up Idle, not Running.
				if(session.Status is SessionStatusEnum.NeedsPermission or SessionStatusEnum.NeedsUserInput)
				{
					session.Status = session.ActiveWorkingGroup is not null
						? SessionStatusEnum.Running
						: SessionStatusEnum.Idle;
				}

				// Remove from registry inside the lock to stop event delivery on stale state.
				_sdkRegistry.TryRemove(session.Id, out sdkSession);
				session.MessagesSnapshot = [.. session.Messages];
			}

			// Clear blocking-request state outside SessionEventLock to avoid lock-ordering issues.
			lock(session.StatusHistoryLock)
			{
				session.StatusHistory.Clear();
			}

			session.PendingPermissionRequests.Clear();
			session.PendingUserInputRequests.Clear();

			_permissionHandler.CancelPendingRequestsForSession(session.Id);
			_userInputHandler.CancelPendingRequestsForSession(session.Id);

			if(sdkSession is not null)
			{
				try
				{
					await sdkSession.DisposeAsync().AsTask()
						.WaitAsync(TimeSpan.FromSeconds(disposeTimeoutSeconds));
				}
				catch(TimeoutException)
				{
					_logger.LogWarning("Timed out disposing SDK session {SessionId} after disconnect", session.Id);
				}
				catch(Exception ex)
				{
					_logger.LogWarning(ex, "Error disposing SDK session {SessionId} after disconnect", session.Id);
				}
			}
		}

		_sessionListFeature.NotifyStateChanged();
		_logger.LogInformation("SDK state reset complete after client disconnect");
	}

	/// <summary>
	/// Called when the SDK client is successfully recreated after a disconnect.
	/// If the current session had an active working group, performs a silent resume so the
	/// AI continues seamlessly. Otherwise falls back to a full <see cref="LoadSession"/>
	/// which replays history cleanly.
	/// </summary>
	async Task HandleClientReconnectedAsync()
	{
		_logger.LogInformation("Client reconnected — resuming current session");

		SessionModel? current = _sessionListFeature.CurrentSession;
		if(current is null || current.SdkState != SdkSessionStateEnum.NotLoaded)
		{
			return;
		}

		bool wasActivelyWorking = current.ActiveWorkingGroup is not null
			&& current.ActiveWorkingGroup.Status == GroupStatusEnum.Running;

		if(wasActivelyWorking)
		{
			await SilentResumeSessionAsync(current);
		}
		else
		{
			try
			{
				await LoadSession(current.Id);
			}
			catch(Exception ex)
			{
				_logger.LogWarning(ex, "Failed to reload current session {SessionId} after reconnect", current.Id);
			}
		}
	}

	/// <summary>
	/// Reconnects a session that was actively working when the client disconnected, without
	/// replaying history or closing the working panel. Sends a hidden continuation prompt so
	/// the AI picks up from where it left off. The hidden prompt is identified by
	/// <see cref="SessionEventProcessor.ReconnectContinuationPrefix"/> and is suppressed at the
	/// event-processor level — it never appears in the chat log and does not trigger the
	/// safety-net that would otherwise close the working group.
	/// </summary>
	async Task SilentResumeSessionAsync(SessionModel session)
	{
		_logger.LogInformation("Silently resuming session {SessionId} after reconnect", session.Id);

		CopilotSession? sdkSession = null;
		try
		{
			session.SdkState = SdkSessionStateEnum.Loading;
			_sessionListFeature.NotifyStateChanged();

			ResumeSessionConfig config = new()
			{
				ClientName = "Cockpit",
				EnableConfigDiscovery = true,
				Model = session.Model.Id,
				ReasoningEffort = session.ReasoningEffort,
				Streaming = true,
				// DisableResume=true suppresses the session.resume event so history is not
				// replayed — the existing session.Messages stays intact.
				DisableResume = true,
				WorkingDirectory = session.Context.CurrentWorkingDirectory,
				OnPermissionRequest = _permissionHandler.HandlePermissionRequest,
				OnUserInputRequest = _userInputHandler.HandleUserInputRequest,
				Hooks = _hooksFactory.CreateHooks(session.Model.Id, session.ReasoningEffort, session.Context.CurrentWorkingDirectory, disableResume: true)
			};

			CopilotClient client = await _clientFeature.GetClientAsync();
			sdkSession = await client.ResumeSessionAsync(session.Id, config);

			_sdkRegistry.Register(sdkSession, evt =>
			{
				_logger.LogDebug("Session {SessionId} event: {EventType}", sdkSession.SessionId, evt.Type);
				HandleSessionEvent(sdkSession.SessionId, evt);
			});

			session.SdkState = SdkSessionStateEnum.Resumed;
			_sessionListFeature.NotifyStateChanged();

			// Send an internal continuation prompt. The content prefix causes SessionEventProcessor
			// to suppress both the live echo and any future replay of this message.
			await sdkSession.SendAsync(new MessageOptions
			{
				Prompt = SessionEventProcessor.ReconnectContinuationPrefix + " Session was briefly disconnected, please continue from where you left off.",
				Mode = MessageTurnModeExtensions.ImmediateSdkToken
			});

			_logger.LogInformation("Silently resumed session {SessionId}", session.Id);
		}
		catch(Exception ex)
		{
			_logger.LogWarning(ex, "Silent resume failed for session {SessionId}; falling back to full reload", session.Id);

			// Clean up any partially-registered session to avoid a stale entry in the registry.
			if(sdkSession is not null)
			{
				_sdkRegistry.TryRemove(session.Id, out _);
				try
				{
					await sdkSession.DisposeAsync();
				}
				catch { }
			}

			session.SdkState = SdkSessionStateEnum.NotLoaded;

			// Fallback: finalize the broken working group visually and do a full reload.
			lock(session.SessionEventLock)
			{
				if(session.ActiveWorkingGroup is not null)
				{
					SessionIdleHandler.Handle(session, groupStatus: GroupStatusEnum.Error);
				}

				session.MessagesSnapshot = [.. session.Messages];
			}

			_sessionListFeature.NotifyStateChanged();

			try
			{
				await LoadSession(session.Id);
			}
			catch(Exception loadEx)
			{
				_logger.LogWarning(loadEx, "Fallback reload also failed for session {SessionId}", session.Id);
			}
		}
	}
}
