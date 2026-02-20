using Cockpit.Features.SessionEvents.Handlers;
using Cockpit.Features.SessionEvents.Models;
using Cockpit.Models;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging;

namespace Cockpit.Features.SessionEvents;

/// <summary>
/// Entry point for all SDK session event processing. Routes events to the appropriate static handler.
/// Handlers mutate <see cref="ChatSession"/> state only — UI notification is the caller's responsibility.
/// </summary>
public sealed class SessionEventProcessor
{
	readonly ILogger<SessionEventProcessor> _logger;

	public SessionEventProcessor(ILogger<SessionEventProcessor> logger)
	{
		_logger = logger;
	}

	/// <summary>
	/// Processes a session event, mutating <paramref name="session"/> state.
	/// Pass <paramref name="onStreamSummary"/> to stream the idle-event summary progressively;
	/// pass <c>null</c> for background (non-visible) sessions.
	/// </summary>
	public void Process(ChatSession session, SessionEvent evt, Func<ChatMessageModel, string, Task>? onStreamSummary = null)
	{
		try
		{
			switch(evt)
			{
				case UserMessageEvent userMsg:
					// Capture whether the agent was mid-turn before the safety-net finalises the group
					bool wasAgentBusy = session.ActiveWorkingGroup is not null;
					// Safety net: finalize any prior group not yet closed by SessionIdleEvent
					if(wasAgentBusy)
					{
						SessionIdleHandler.Handle(session);
					}
					UserMessageHandler.Handle(session, userMsg, wasAgentBusy);
					break;

				case AssistantTurnStartEvent:
					AssistantTurnStartHandler.Handle(session);
					break;

				case AssistantMessageDeltaEvent deltaMsg:
					AssistantMessageDeltaHandler.Handle(session, deltaMsg);
					break;

				case AssistantMessageEvent assistantMsg:
					AssistantMessageHandler.Handle(session, assistantMsg);
					break;

				case AssistantReasoningDeltaEvent reasoningDelta:
					//////	AssistantReasoningDeltaHandler.Handle(session, reasoningDelta);
					break;

				case AssistantReasoningEvent reasoning:
					AssistantReasoningHandler.Handle(session, reasoning);
					break;

				case ToolExecutionStartEvent toolStart:
					ToolStartHandler.Handle(session, toolStart);
					break;

				case ToolExecutionCompleteEvent toolComplete:
					ToolCompleteHandler.Handle(session, toolComplete);
					break;

				case ToolExecutionProgressEvent toolProgress:
					ToolProgressHandler.Handle(session, toolProgress);
					break;

				case ToolExecutionPartialResultEvent toolPartial:
					ToolPartialResultHandler.Handle(session, toolPartial);
					break;

				case SubagentStartedEvent subagentStarted:
					SubagentStartedHandler.Handle(session, subagentStarted);
					break;

				case SubagentCompletedEvent subagentCompleted:
					SubagentCompletedHandler.Handle(session, subagentCompleted);
					break;

				case SubagentFailedEvent subagentFailed:
					SubagentFailedHandler.Handle(session, subagentFailed);
					break;

				case SessionIdleEvent idleEvt:
					SessionIdleHandler.Handle(session, idleEvt.Timestamp, onStreamSummary);
					break;

				case SessionErrorEvent error:
					SessionErrorHandler.Handle(session, error);
					break;

				case SessionTitleChangedEvent titleChanged:
					SessionTitleChangedHandler.Handle(session, titleChanged);
					break;

				case AbortEvent abort:
					AbortHandler.Handle(session, abort, _logger);
					break;

				case SessionShutdownEvent shutdown:
					SessionShutdownHandler.Handle(session, shutdown, _logger);
					break;

				case SessionWarningEvent warning:
					SessionWarningHandler.Handle(session, warning, _logger);
					break;

				case SessionCompactionStartEvent:
					_logger.LogInformation("Session {SessionId} started context compaction", session.Id);
					break;

				case SessionCompactionCompleteEvent compaction:
					_logger.LogInformation("Session {SessionId} completed compaction: {TokensRemoved} tokens removed",
						session.Id, compaction.Data?.TokensRemoved);
					break;

				// Tier 2 — informational logging only
				case AssistantIntentEvent intent:
					_logger.LogDebug("Session {SessionId} assistant intent: {Intent}", session.Id, intent.Data?.Intent);
					break;

				case AssistantTurnEndEvent turnEnd:
					_logger.LogDebug("Session {SessionId} assistant turn ended: {TurnId}", session.Id, turnEnd.Data?.TurnId);
					break;

				case AssistantUsageEvent usage:
					_logger.LogDebug("Session {SessionId} usage — model: {Model}, in: {In}, out: {Out}, cost: {Cost}",
						session.Id, usage.Data?.Model, usage.Data?.InputTokens, usage.Data?.OutputTokens, usage.Data?.Cost);
					break;

				case SessionInfoEvent info:
					_logger.LogInformation("Session {SessionId} info [{InfoType}]: {Message}",
						session.Id, info.Data?.InfoType, info.Data?.Message);
					break;

				case SessionStartEvent start:
					_logger.LogInformation("Session {SessionId} started — producer: {Producer}, model: {Model}",
						session.Id, start.Data?.Producer, start.Data?.SelectedModel);
					break;

				case SessionResumeEvent resume:
					_logger.LogInformation("Session {SessionId} resumed at {ResumeTime}, {EventCount} prior events",
						session.Id, resume.Data?.ResumeTime, resume.Data?.EventCount);
					break;

				case SessionContextChangedEvent ctxChanged:
					_logger.LogInformation("Session {SessionId} context changed — cwd: {Cwd}, repo: {Repo}, branch: {Branch}",
						session.Id, ctxChanged.Data?.Cwd, ctxChanged.Data?.Repository, ctxChanged.Data?.Branch);
					break;

				case SessionModeChangedEvent modeChanged:
					_logger.LogInformation("Session {SessionId} mode changed: {Prev} → {New}",
						session.Id, modeChanged.Data?.PreviousMode, modeChanged.Data?.NewMode);
					break;

				case SessionModelChangeEvent modelChange:
					_logger.LogInformation("Session {SessionId} model changed: {Prev} → {New}",
						session.Id, modelChange.Data?.PreviousModel, modelChange.Data?.NewModel);
					break;

				case SessionHandoffEvent handoff:
					_logger.LogInformation("Session {SessionId} handoff — source: {Source}, summary: {Summary}",
						session.Id, handoff.Data?.SourceType, handoff.Data?.Summary);
					break;

				case SessionTruncationEvent truncation:
					_logger.LogInformation("Session {SessionId} truncated — {MessagesRemoved} messages, {TokensRemoved} tokens removed",
						session.Id, truncation.Data?.MessagesRemovedDuringTruncation, truncation.Data?.TokensRemovedDuringTruncation);
					break;

				case SessionUsageInfoEvent usageInfo:
					_logger.LogDebug("Session {SessionId} usage info — {Current}/{Limit} tokens, {Messages} messages",
						session.Id, usageInfo.Data?.CurrentTokens, usageInfo.Data?.TokenLimit, usageInfo.Data?.MessagesLength);
					break;

				case SessionPlanChangedEvent planChanged:
					_logger.LogDebug("Session {SessionId} plan changed: {Operation}", session.Id, planChanged.Data?.Operation);
					break;

				case SessionSnapshotRewindEvent snapshotRewind:
					_logger.LogInformation("Session {SessionId} snapshot rewind — {EventsRemoved} events removed",
						session.Id, snapshotRewind.Data?.EventsRemoved);
					break;

				case SessionWorkspaceFileChangedEvent fileChanged:
					_logger.LogDebug("Session {SessionId} workspace file {Operation}: {Path}",
						session.Id, fileChanged.Data?.Operation, fileChanged.Data?.Path);
					break;

				case HookStartEvent hookStart:
					_logger.LogDebug("Session {SessionId} hook started — type: {HookType}, id: {Id}",
						session.Id, hookStart.Data?.HookType, hookStart.Data?.HookInvocationId);
					break;

				case HookEndEvent hookEnd:
					_logger.LogInformation("Session {SessionId} hook ended — type: {HookType}, success: {Success}",
						session.Id, hookEnd.Data?.HookType, hookEnd.Data?.Success);
					break;

				case SkillInvokedEvent skill:
					_logger.LogInformation("Session {SessionId} skill invoked: {Name}", session.Id, skill.Data?.Name);
					break;

				case SubagentSelectedEvent subagentSelected:
					_logger.LogInformation("Session {SessionId} subagent selected: {AgentName}", session.Id, subagentSelected.Data?.AgentName);
					break;

				case SystemMessageEvent systemMsg:
					_logger.LogDebug("Session {SessionId} system message [{Role}]", session.Id, systemMsg.Data?.Role);
					break;

				case ToolUserRequestedEvent toolUserRequested:
					_logger.LogDebug("Session {SessionId} tool user requested: {ToolName} (handled by permission callback)",
						session.Id, toolUserRequested.Data?.ToolName);
					break;

				case PendingMessagesModifiedEvent:
					_logger.LogDebug("Session {SessionId} pending messages modified", session.Id);
					break;

				default:
					_logger.LogDebug("Unhandled event type {EventType} for session {SessionId}", evt.Type, session.Id);
					break;
			}
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Error handling session event {EventType} for session {SessionId}", evt.Type, session.Id);
		}

		// Update LastActivity only for meaningful events (user/assistant messages, tool executions)
		if(evt is UserMessageEvent or AssistantMessageEvent or AssistantTurnEndEvent
			or ToolExecutionStartEvent or ToolExecutionCompleteEvent or SessionIdleEvent
			or SubagentStartedEvent or SubagentCompletedEvent or SessionErrorEvent)
		{
			session.LastActivity = evt.Timestamp.LocalDateTime;
		}
	}

	/// <summary>
	/// Finalizes any open <see cref="ActivityGroup"/> on the session (e.g. after abrupt termination during replay).
	/// </summary>
	public void FinalizeOpenGroup(ChatSession session) => SessionIdleHandler.Handle(session);
}
