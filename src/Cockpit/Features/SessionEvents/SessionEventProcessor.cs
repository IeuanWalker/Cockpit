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
	/// Prefix that identifies an internal reconnect-continuation prompt sent by Cockpit after
	/// a client disconnect. Events whose content starts with this marker are suppressed at the
	/// processor level — they never appear in the chat log and never trigger the safety-net.
	/// The constant lives here so it is shared between <see cref="SessionEventProcessor"/>
	/// and <see cref="Sessions.SessionFeature"/>.
	/// </summary>
	internal const string ReconnectContinuationPrefix = "##COCKPIT-RECONNECT##";

	/// <summary>
	/// Processes a session event, mutating <paramref name="session"/> state.
	/// Pass <paramref name="onStreamSummary"/> to stream the idle-event summary progressively;
	/// pass <c>null</c> for background (non-visible) sessions.
	/// When <paramref name="deferIdle"/> is <c>true</c> (live mode), <c>session.idle</c> moves
	/// the active group to <see cref="SessionModel.PendingFinalizationGroup"/> instead of
	/// finalizing immediately. A subsequent <c>assistant.turn_start</c> recovers it (multi-turn
	/// continuation), any other event finalizes it, and a timer in the feature layer handles
	/// the "no more events" case.
	/// </summary>
	public void Process(SessionModel session, SessionEvent evt, Func<ChatMessageModel, string, Task>? onStreamSummary = null, bool deferIdle = false)
	{
		try
		{
			// Multi-turn recovery / deferred finalization:
			// If a previous session.idle deferred its group, resolve it based on the incoming event.
			// Only "significant" events force immediate finalization — metadata events (usage, intent,
			// info, etc.) pass through so the debounce timer can handle them.
			if(session.PendingFinalizationGroup is not null)
			{
				switch(evt)
				{
					case AssistantTurnStartEvent:
						// Continuation turn — recover the group so tools keep appending to the same panel.
						// During the debounce window, no new pending messages can be created (SendMessageAsync
						// acquires the same lock), so this is always a continuation of the same user prompt.
						_logger.LogDebug("Session {SessionId}: recovering deferred group for multi-turn continuation", session.Id);
						session.ActiveWorkingGroup = session.PendingFinalizationGroup;
						session.ActiveWorkingGroup.Status = GroupStatusEnum.Running;
						session.ActiveWorkingGroup.IsExpanded = true;
						session.PendingFinalizationGroup = null;
						session.PendingFinalizationTimestamp = null;
						session.IdleFinalizationGeneration++;
						session.Status = SessionStatusEnum.Running;
						break; // Fall through to let AssistantTurnStartHandler handle turn activation

					case UserMessageEvent:
						// New user message while debounce was pending — finalize the old group first.
						_logger.LogDebug("Session {SessionId}: finalizing deferred group before user message", session.Id);
						FinalizePendingGroup(session, onStreamSummary);
						break;

					case AbortEvent:
					case SessionErrorEvent:
						// Error/abort — finalize with Error status.
						_logger.LogDebug("Session {SessionId}: finalizing deferred group as error before {EventType}", session.Id, evt.GetType().Name);
						FinalizePendingGroup(session, onStreamSummary, GroupStatusEnum.Error);
						break;

					case SessionShutdownEvent shut when shut.Data.ShutdownType != ShutdownType.Routine:
						// Non-routine (fatal) shutdown — finalize with Error status.
						_logger.LogDebug("Session {SessionId}: finalizing deferred group as error before non-routine shutdown", session.Id);
						FinalizePendingGroup(session, onStreamSummary, GroupStatusEnum.Error);
						break;

					case SessionShutdownEvent:
						// Routine shutdown — session will restart, discard the pending group.
						_logger.LogDebug("Session {SessionId}: discarding deferred group on routine shutdown", session.Id);
						session.PendingFinalizationGroup = null;
						session.PendingFinalizationTimestamp = null;
						break;

					// All other events (metadata: usage, intent, info, turn_end, etc.):
					// Leave the pending group alone — the debounce timer will finalize it.
				}
			}

			switch(evt)
			{
				// Agent-synthesised continuation: keep the working panel open and suppress
				// the safety-net, completion sound, and chat-log entry.
				case UserMessageEvent userMsg when userMsg.Data?.Source == "thinking-exhausted-continuation":
					_logger.LogDebug("Session {SessionId} thinking-exhausted continuation", session.Id);
					ThinkingExhaustedContinuationHandler.Handle(session, userMsg);
					break;

				// Cockpit-internal reconnect continuation: silently re-triggers the AI after a
				// client disconnect. Must not appear in chat, not close the working panel, and
				// not trigger the safety-net — handled identically to thinking-exhausted-continuation.
				case UserMessageEvent reconnectMsg when reconnectMsg.Data?.Content?.StartsWith(ReconnectContinuationPrefix, StringComparison.Ordinal) == true:
					_logger.LogDebug("Session {SessionId} reconnect-continuation suppressed", session.Id);
					session.Status = SessionStatusEnum.Running;
					break;

				case UserMessageEvent userMsg:
					// Determine whether the agent was genuinely mid-turn before the safety-net runs.
					//
					// Background: in immediate-mode, the SDK writes assistant.turn_start to the event log
					// *before* the user.message echo. The 100 ms swap heuristic in ReorderImmediateModeReplayEvents
					// corrects this for most cases, but can fail when there is a larger gap. When it fails,
					// AssistantTurnStartHandler creates a working group *before* the user message is in
					// session.Messages. That group is empty at the point the user.message event is processed.
					//
					// Old behaviour: fire the safety-net unconditionally → empty group cleared → subsequent
					// assistant messages (turn 0) go to chat → tools anchor to that chat message → ops group
					// appears *between* two assistant messages instead of below the user prompt.
					//
					// Fix: if the open group is empty (no tools, no non-empty thinking messages) it was
					// spuriously created by the premature turn_start. Keep it alive and re-anchor it to
					// this user message so that all subsequent assistant messages and tool calls are
					// correctly attributed to this prompt.
					//
					// Safety for live mode: user.message echoes are not emitted in the live SDK event
					// stream — only written to disk for replay. ThinkingExhausted/reconnect continuations
					// are handled by the earlier case guards above and never reach this branch.
					// wasAgentBusy only affects the non-optimistic (replay) code path inside
					// UserMessageHandler; live sends set IsPending at send time and it is preserved.
					ActivityGroupModel? priorGroup = session.ActiveWorkingGroup;
					bool wasAgentBusy = false;
					bool isSpuriousEmptyGroup = false;
					if(priorGroup is not null)
					{
						List<ThinkingEventModel> priorEvents = priorGroup.GetEventsSnapshot();
						wasAgentBusy = priorEvents.Any(e =>
							(e.Type == ThinkingEventTypeEnum.Tool && e.Tool is not null)
							|| ((e.Type == ThinkingEventTypeEnum.Message || e.Type == ThinkingEventTypeEnum.Reasoning)
								&& !string.IsNullOrWhiteSpace(e.Message)));
						if(!wasAgentBusy)
						{
							// Empty spurious group — keep it alive; re-anchor after the user message is added
							isSpuriousEmptyGroup = true;
						}
					}
					string content = userMsg.Data?.Content ?? string.Empty;
					_logger.LogDebug("Session {SessionId} user message: {Content}", session.Id, content[..Math.Min(50, content.Length)]);
					// Add the user message FIRST so it appears before operations in the message list.
					// The safety-net finalization below will anchor past it when extending over
					// consecutive user messages, producing the desired grouping:
					// [user msg 1] [user msg 2 (enqueued)] [operations] [agent response]
					UserMessageHandler.Handle(session, userMsg, wasAgentBusy);
					if(wasAgentBusy)
					{
						// Safety net: finalize the prior group now that the user message is positioned
						SessionIdleHandler.Handle(session, logger: _logger);
					}
					if(isSpuriousEmptyGroup && session.ActiveWorkingGroup == priorGroup)
					{
						// Re-anchor the group to the user message just added to session.Messages
						ChatMessageModel? addedMsg = session.Messages.LastOrDefault(m => m.IsUser && m.IsComplete && !m.IsPending);
						if(addedMsg is not null)
						{
							priorGroup!.TriggeredByUserMessageId = addedMsg.Id;
						}
					}
					session.LastActivity = userMsg.Timestamp.UtcDateTime;
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
					_logger.LogDebug("Session {SessionId} idle (deferIdle={DeferIdle})", session.Id, deferIdle);
					if(deferIdle && session.ActiveWorkingGroup is not null)
					{
						// Live mode: defer finalization to allow multi-turn continuations to reuse the group.
						// The feature layer starts a timer; if no turn_start arrives, it calls FinalizeIfPending.
						bool hasPendingMessages = session.Messages.Any(m => m.IsUser && m.IsPending);
						if(hasPendingMessages)
						{
							// Enqueue mode: keep running — next turn_start will activate the pending message.
							session.Status = SessionStatusEnum.Running;
						}
						else
						{
							session.PendingFinalizationGroup = session.ActiveWorkingGroup;
							session.PendingFinalizationTimestamp = idleEvt.Timestamp;
							session.ActiveWorkingGroup = null;
							// Keep Status = Running during the debounce window so the session
							// appears "busy" (prevents new messages from being treated as immediate,
							// keeps IsWorking true). The timer or next finalization event will set Idle.
							session.IdleFinalizationGeneration++;
						}
					}
					else
					{
						// Immediate finalization (test mode / replay / no active group).
						SessionIdleHandler.Handle(session, idleEvt.Timestamp, onStreamSummary, logger: _logger);
					}
					session.LastActivity = idleEvt.Timestamp.UtcDateTime;
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

	/// <summary>
	/// Finalizes the deferred <see cref="SessionModel.PendingFinalizationGroup"/> if one exists.
	/// Called by the feature-layer timer when no continuation arrives within the debounce window,
	/// and on session shutdown/eviction/error.
	/// </summary>
	/// <returns><c>true</c> if a group was finalized; <c>false</c> if nothing was pending.</returns>
	public bool FinalizeIfPending(SessionModel session, Func<ChatMessageModel, string, Task>? onStreamSummary = null)
	{
		if(session.PendingFinalizationGroup is null)
		{
			return false;
		}

		FinalizePendingGroup(session, onStreamSummary);
		return true;
	}

	/// <summary>
	/// Internal helper — moves <see cref="SessionModel.PendingFinalizationGroup"/> back to
	/// <see cref="SessionModel.ActiveWorkingGroup"/> and calls <see cref="SessionIdleHandler.Handle"/>.
	/// </summary>
	void FinalizePendingGroup(SessionModel session, Func<ChatMessageModel, string, Task>? onStreamSummary = null, GroupStatusEnum groupStatus = GroupStatusEnum.Complete)
	{
		session.ActiveWorkingGroup = session.PendingFinalizationGroup;
		DateTimeOffset ts = session.PendingFinalizationTimestamp ?? DateTimeOffset.UtcNow;
		session.PendingFinalizationGroup = null;
		session.PendingFinalizationTimestamp = null;
		SessionIdleHandler.Handle(session, ts, onStreamSummary, groupStatus, _logger);
		session.LastActivity = ts.UtcDateTime;
	}
}
