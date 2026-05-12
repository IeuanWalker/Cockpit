using Cockpit.Features.SessionEvents.Handlers;
using Cockpit.Features.SessionEvents.Models;
using Cockpit.Features.Sessions.Models;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging;

namespace Cockpit.Features.SessionEvents;

/// <summary>
/// Entry point for all SDK session event processing. Routes events to the appropriate static handler.
/// Handlers mutate <see cref="SessionModel"/> state only — UI notification is the caller's responsibility.
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
	public void Process(SessionModel session, SessionEvent evt, Func<ChatMessageModel, string, Task>? onStreamSummary = null)
	{
		try
		{
			switch(evt)
			{
				// Agent-synthesised continuation: keep the working panel open and suppress
				// the safety-net, completion sound, and chat-log entry.
				case UserMessageEvent userMsg when userMsg.Data?.Source == "thinking-exhausted-continuation":
					_logger.LogDebug("Session {SessionId} thinking-exhausted continuation", session.Id);
					ThinkingExhaustedContinuationHandler.Handle(session, userMsg);
					break;

				case UserMessageEvent userMsg:
					// Capture whether the agent was mid-turn before the safety-net finalises the group
					bool wasAgentBusy = session.ActiveWorkingGroup is not null;
					// Safety net: finalize any prior group not yet closed by SessionIdleEvent
					if(wasAgentBusy)
					{
						SessionIdleHandler.Handle(session, logger: _logger);
					}
					string content = userMsg.Data?.Content ?? string.Empty;
					_logger.LogDebug("Session {SessionId} user message: {Content}", session.Id, content[..Math.Min(50, content.Length)]);
					UserMessageHandler.Handle(session, userMsg, wasAgentBusy);
					session.LastActivity = userMsg.Timestamp.LocalDateTime;
					break;

				case AssistantTurnStartEvent turnStart:
					_logger.LogDebug("Session {SessionId} assistant turn started: {TurnId}", session.Id, turnStart.Data?.TurnId);
					AssistantTurnStartHandler.Handle(session, turnStart);
					break;

				case AssistantMessageDeltaEvent deltaMsg:
					_logger.LogDebug("Session {SessionId} assistant message delta: {MessageId}", session.Id, deltaMsg.Data?.MessageId);
					AssistantMessageDeltaHandler.Handle(session, deltaMsg);
					break;

				case AssistantMessageEvent assistantMsg:
					_logger.LogDebug("Session {SessionId} assistant message: {MessageId}", session.Id, assistantMsg.Data?.MessageId);
					AssistantMessageHandler.Handle(session, assistantMsg);
					break;

				case AssistantReasoningDeltaEvent reasoningDelta:
					_logger.LogDebug("Session {SessionId} assistant reasoning delta", session.Id);
					AssistantReasoningDeltaHandler.Handle(session, reasoningDelta);
					break;

				case AssistantReasoningEvent reasoning:
					_logger.LogDebug("Session {SessionId} assistant reasoning", session.Id);
					AssistantReasoningHandler.Handle(session, reasoning);
					break;

				case ToolExecutionStartEvent toolStart:
					_logger.LogDebug("Session {SessionId} tool start: {ToolName}", session.Id, toolStart.Data?.ToolName);
					ToolStartHandler.Handle(session, toolStart);
					break;

				case ToolExecutionCompleteEvent toolComplete:
					_logger.LogDebug("Session {SessionId} tool complete: {ToolCallId}", session.Id, toolComplete.Data?.ToolCallId);
					ToolCompleteHandler.Handle(session, toolComplete);
					break;

				case ToolExecutionProgressEvent toolProgress:
					_logger.LogDebug("Session {SessionId} tool progress: {ToolCallId}", session.Id, toolProgress.Data?.ToolCallId);
					ToolProgressHandler.Handle(session, toolProgress);
					break;

				case ToolExecutionPartialResultEvent toolPartial:
					_logger.LogDebug("Session {SessionId} tool partial result: {ToolCallId}", session.Id, toolPartial.Data?.ToolCallId);
					ToolPartialResultHandler.Handle(session, toolPartial);
					break;

				case SubagentStartedEvent subagentStarted:
					_logger.LogDebug("Session {SessionId} subagent started: {AgentName}", session.Id, subagentStarted.Data?.AgentName);
					SubagentStartedHandler.Handle(session, subagentStarted);
					break;

				case SubagentCompletedEvent subagentCompleted:
					_logger.LogDebug("Session {SessionId} subagent completed: {AgentName}", session.Id, subagentCompleted.Data?.AgentName);
					SubagentCompletedHandler.Handle(session, subagentCompleted);
					break;

				case SubagentFailedEvent subagentFailed:
					_logger.LogDebug("Session {SessionId} subagent failed: {AgentName}", session.Id, subagentFailed.Data?.AgentName);
					SubagentFailedHandler.Handle(session, subagentFailed);
					break;

				case SessionTaskCompleteEvent taskComplete:
					_logger.LogDebug("Session {SessionId} task complete — success: {Success}", session.Id, taskComplete.Data?.Success);
					SessionTaskCompleteHandler.Handle(session, taskComplete);
					break;

				case SessionIdleEvent idleEvt:
					_logger.LogDebug("Session {SessionId} idle", session.Id);
					SessionIdleHandler.Handle(session, idleEvt.Timestamp, onStreamSummary, logger: _logger);
					session.LastActivity = idleEvt.Timestamp.LocalDateTime;
					break;

				case SessionErrorEvent error:
					_logger.LogDebug("Session {SessionId} error: {Message}", session.Id, error.Data?.Message);
					SessionErrorHandler.Handle(session, error);
					break;

				case SessionTitleChangedEvent titleChanged:
					_logger.LogDebug("Session {SessionId} title changed: {Title}", session.Id, titleChanged.Data?.Title);
					SessionTitleChangedHandler.Handle(session, titleChanged);
					break;

				case AbortEvent abort:
					_logger.LogDebug("Session {SessionId} abort", session.Id);
					AbortHandler.Handle(session, abort, _logger);
					break;

				case SessionShutdownEvent shutdown:
					_logger.LogDebug("Session {SessionId} shutdown", session.Id);
					SessionShutdownHandler.Handle(session, shutdown, _logger);
					break;

				case SessionWarningEvent warning:
					_logger.LogDebug("Session {SessionId} warning: {Message}", session.Id, warning.Data?.Message);
					SessionWarningHandler.Handle(session, warning, _logger);
					break;

				case SessionCompactionStartEvent:
					_logger.LogInformation("Session {SessionId} started context compaction", session.Id);
					session.IsCompacting = true;
					break;

				case SessionCompactionCompleteEvent compaction:
					_logger.LogInformation("Session {SessionId} completed compaction: {TokensRemoved} tokens removed",
						session.Id, compaction.Data?.TokensRemoved);
					session.IsCompacting = false;
					break;

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
					SessionContextChangedHandler.Handle(session, ctxChanged);
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
					SessionUsageInfoHandler.Handle(session, usageInfo);
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

				case SubagentDeselectedEvent:
					_logger.LogInformation("Session {SessionId} subagent deselected, returning to default agent", session.Id);
					break;

				case SystemMessageEvent systemMsg:
					_logger.LogDebug("Session {SessionId} system message [{Role}]", session.Id, systemMsg.Data?.Role);
					break;

				case ToolUserRequestedEvent toolUserRequested:
					_logger.LogDebug("Session {SessionId} tool user requested: {ToolName} (handled by permission callback)",
						session.Id, toolUserRequested.Data?.ToolName);
					break;

				case PendingMessagesModifiedEvent:
					session.PendingMessageCount = session.Messages.Count(m => m.IsUser && m.IsPending);
					_logger.LogDebug("Session {SessionId} pending messages modified — count: {PendingCount}",
						session.Id, session.PendingMessageCount);
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
	}

	/// <summary>
	/// Finalizes any open <see cref="ActivityGroup"/> on the session (e.g. after abrupt termination during replay).
	/// </summary>
	public void FinalizeOpenGroup(SessionModel session) => SessionIdleHandler.Handle(session, logger: _logger);
}
