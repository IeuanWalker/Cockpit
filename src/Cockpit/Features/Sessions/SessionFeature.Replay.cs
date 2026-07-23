using System.Text.Json;
using Cockpit.Features.Agents.Models;
using Cockpit.Features.Canvas;
using Cockpit.Features.Git.Models;
using Cockpit.Features.Permissions;
using Cockpit.Features.SessionEvents;
using Cockpit.Features.SessionEvents.Models;
using Cockpit.Features.Sessions.Interactions;
using Cockpit.Features.Sessions.Models;
using Cockpit.Features.SystemMessage;
using GitHub.Copilot;
using GitHub.Copilot.Rpc;
using Microsoft.Extensions.Logging;
using SdkPlugin = GitHub.Copilot.Rpc.Plugin;
using SdkSessionMetadata = GitHub.Copilot.SessionMetadata;

namespace Cockpit.Features.Sessions;

public sealed partial class SessionFeature
{
	/// <summary>
	/// Debug helper: clears the current session's messages and replays them from the SDK history,
	/// introducing timestamp-proportional delays between events so the replay feels like a live session.
	/// </summary>
	public async Task ReplayCurrentSessionAsync(CancellationToken cancellationToken = default)
	{
		SessionModel? session = _sessionListFeature.CurrentSession;
		if(session is null)
		{
			_logger.LogWarning("ReplayCurrentSession: no current session");
			return;
		}

		if(!_sdkRegistry.TryGet(session.Id, out CopilotSession? sdkSession))
		{
			_logger.LogWarning("ReplayCurrentSession: SDK session not found for {SessionId}", session.Id);
			return;
		}

		try
		{
			IReadOnlyList<SessionEvent> events = await sdkSession.GetEventsAsync(cancellationToken);
			_logger.LogInformation("Replaying {Count} events for session {SessionId}", events.Count, session.Id);

			lock(session.SessionEventLock)
			{
				session.Conversation.ClearMessages();
				session.ActiveWorkingGroup = null;
			}
			_sessionListFeature.NotifyStateChanged();

			// Same parentId-based immediate-mode detection used during live sessions applies
			// here — no pre-processing or reordering needed.
			Task streamCallback(ChatMessageModel msg, string text) => SessionEventHelpers.StreamSummaryTextAsync(msg, text, _sessionListFeature.NotifyStateChanged);

			DateTimeOffset? prevTimestamp = null;
			foreach(SessionEvent evt in events)
			{
				cancellationToken.ThrowIfCancellationRequested();

				if(prevTimestamp.HasValue)
				{
					TimeSpan realGap = evt.Timestamp - prevTimestamp.Value;
					int delayMs = (int)Math.Clamp(realGap.TotalMilliseconds, 50, 3000);
					await Task.Delay(delayMs, cancellationToken);
				}

				lock(session.SessionEventLock)
				{
					_processor.Process(session, evt, streamCallback);
					session.Conversation.PublishMessagesSnapshot();
				}
				_sessionListFeature.NotifyStateChanged();

				prevTimestamp = evt.Timestamp;
			}

			lock(session.SessionEventLock)
			{
				if(session.ActiveWorkingGroup is not null)
				{
					_processor.FinalizeOpenGroup(session);
				}
				session.Conversation.PublishMessagesSnapshot();
			}
			_sessionListFeature.NotifyStateChanged();

			_logger.LogInformation("Replay complete for session {SessionId} — {MessageCount} messages", session.Id, session.Messages.Count);
		}
		catch(OperationCanceledException)
		{
			_logger.LogInformation("Replay cancelled for session {SessionId}", session.Id);
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Replay failed for session {SessionId}", session.Id);
		}
	}

}
