# SessionEvents Feature

## Purpose

The **SessionEvents** feature is responsible for translating raw SDK events from `GitHub.Copilot.SDK`
into model mutations on `SessionModel`. It is the bridge between the Copilot SDK event stream and
the application's UI state.

All SDK events arrive through `SessionEventProcessor.Process()`, which routes each event type to a
dedicated static handler. Handlers **only mutate `SessionModel`** — they never raise UI notifications.
The caller (`UnifiedSessionManager`) is responsible for refreshing the UI after `Process()` returns.

---

## Key Classes

| Class | Role |
|---|---|
| `SessionEventProcessor` | Entry point. Routes each `SessionEvent` to the appropriate static handler via a `switch` expression. Catches and logs exceptions so one bad event cannot crash the session. |
| `SessionIdleHandler` | Finalises the active `ActivityGroupModel`, marks stuck tools as `Error`, extracts a summary message, inserts the activity message at the correct position, and raises `OnSessionFinished`. |
| `AssistantMessageDeltaHandler` | Handles streaming message tokens. Without an active group, appends the delta to `Messages`; with an active group, routes content live into the thinking panel. |
| `AssistantMessageHandler` | Finalises a streaming message after all deltas have arrived. Promotes the `StreamingMessages` buffer to a completed `ChatMessageModel` and cleans up `StreamingThinkingEvents`. |
| `AssistantReasoningDeltaHandler` | Accumulates streaming reasoning tokens into a `ThinkingEventModel` of type `Reasoning` inside the active group so reasoning is visible while it streams. |
| `AssistantReasoningHandler` | Finalises the reasoning block. Overrides any delta-accumulated content with the canonical, complete text from the SDK. |
| `AssistantTurnStartHandler` | Creates a fresh `ActivityGroupModel` for the new turn. Records `TriggeredByUserMessageId` and clears the pending flag on the user message that triggered the turn. |
| `ToolStartHandler` | Opens an `ActivityGroupModel` if none is open, then appends a `ThinkingEventModel` (type `Tool`) wrapping a new `ToolExecutionModel`. Nests child tools under their parent when `AgentId` is set. |
| `ToolCompleteHandler` | Locates the matching `ToolExecutionModel` by `ToolCallId` and sets its status to `Success` or `Error`. |
| `ToolProgressHandler` | Appends progress text to the matching `ToolExecutionModel.ProgressMessage`. |
| `ToolPartialResultHandler` | Stores partial output on the matching `ToolExecutionModel.Output`. |
| `SessionTaskCompleteHandler` | Stores the task summary in `SessionModel.PendingTaskSummary` for `SessionIdleHandler` to consume as the preferred summary source. |
| `SessionErrorHandler` | Creates a `ChatMessageModel` of type `Error` and sets `SessionStatusEnum.Error`. |
| `SessionShutdownHandler` | For `ShutdownType.Routine` (auto-restart), does nothing. For other shutdown types, delegates to `SessionIdleHandler` to finalise the session. |
| `AbortHandler` | Finalises the active group with `GroupStatusEnum.Error`, appends a "Session aborted" event, and clears any pending user messages. |
| `SessionTitleChangedHandler` | Updates `SessionModel.Title`. |
| `SessionWarningHandler` | Logs the warning and (for rate-limit warnings) records it for display. |
| `SubagentStartedHandler` | Opens a `ToolExecutionModel` representing the background sub-agent. |
| `SubagentCompletedHandler` | Marks the sub-agent tool as `Success`. |
| `SubagentFailedHandler` | Marks the sub-agent tool as `Error`. |
| `UserMessageHandler` | Appends the echoed user message to `session.Messages`, setting `IsPending` when the agent was already busy. |

---

## SDK Event Flow

```
SDK Event (SessionEvent)
        │
        ▼
SessionEventProcessor.Process(session, evt, onStreamSummary?)
        │
        ├─ UserMessageEvent ──────────────────► UserMessageHandler
        │                                            (safety-net: SessionIdleHandler if busy)
        ├─ AssistantTurnStartEvent ───────────► AssistantTurnStartHandler
        ├─ AssistantMessageDeltaEvent ────────► AssistantMessageDeltaHandler
        │                                            (→ Messages  OR  → thinking panel)
        ├─ AssistantMessageEvent ─────────────► AssistantMessageHandler
        ├─ AssistantReasoningDeltaEvent ──────► AssistantReasoningDeltaHandler
        ├─ AssistantReasoningEvent ───────────► AssistantReasoningHandler
        ├─ ToolExecutionStartEvent ───────────► ToolStartHandler
        ├─ ToolExecutionCompleteEvent ────────► ToolCompleteHandler
        ├─ ToolExecutionProgressEvent ────────► ToolProgressHandler
        ├─ ToolExecutionPartialResultEvent ───► ToolPartialResultHandler
        ├─ SubagentStartedEvent ──────────────► SubagentStartedHandler
        ├─ SubagentCompletedEvent ────────────► SubagentCompletedHandler
        ├─ SubagentFailedEvent ───────────────► SubagentFailedHandler
        ├─ SessionTaskCompleteEvent ──────────► SessionTaskCompleteHandler
        │                                            (stores PendingTaskSummary)
        ├─ SessionIdleEvent ──────────────────► SessionIdleHandler
        │                                            └─ OnSessionFinished? (if Complete & !Suppress)
        ├─ SessionErrorEvent ─────────────────► SessionErrorHandler
        ├─ SessionShutdownEvent ──────────────► SessionShutdownHandler
        │                                            └─ SessionIdleHandler (non-Routine only)
        ├─ AbortEvent ────────────────────────► AbortHandler
        │                                            └─ SessionIdleHandler (Error status)
        ├─ SessionTitleChangedEvent ──────────► SessionTitleChangedHandler
        ├─ SessionWarningEvent ───────────────► SessionWarningHandler
        └─ (all other events) ────────────────► logged only
```

After each successful `Process()` call, `LastActivity` is updated on `UserMessageEvent` and
`SessionIdleEvent`.

---

## ActivityGroup Batching Design

An `ActivityGroupModel` groups all tool executions and thinking events for a single assistant turn.
It drives the collapsible **Operations** panel visible in the UI while the agent is working.

### Group lifecycle

| Event | Effect on group |
|---|---|
| `AssistantTurnStartEvent` | Creates a fresh group with `Status = Running`; sets `TriggeredByUserMessageId` to the pending user message that is cleared by this turn. |
| `ToolExecutionStartEvent` | If no group is open, creates one. Appends a `ThinkingEventModel(Tool)` wrapping a new `ToolExecutionModel`. Child tools (AgentId set) are nested under their parent. |
| `AssistantMessageDeltaEvent` (active group) | Appends / updates a `ThinkingEventModel(Message)` live, making content visible while the assistant is thinking. |
| `AssistantReasoningDeltaEvent` (active group) | Appends / accumulates a `ThinkingEventModel(Reasoning)` for real-time reasoning display. |
| `SessionIdleEvent` | **Finalises** the group: marks stuck tools as `Error`, sets `group.Status = Complete`, collapses the group, inserts an `ActivityGroup` chat message at the correct anchor position, extracts the last message event as a summary, and raises `OnSessionFinished`. |
| `AbortEvent` | Finalises with `GroupStatusEnum.Error`; appends "Session aborted". |
| `SessionShutdownEvent` (non-routine) | Delegates to `SessionIdleHandler.Handle` with default `Complete` status. |

### Summary extraction priority

When the group is finalised, `SessionIdleHandler` selects the summary message in this order:

1. **`PendingTaskSummary`** — set by `SessionTaskCompleteHandler` from a `session.task_complete` event.
2. **Last `ThinkingEventModel` of type `Message`** — extracted from the group's event list and removed.
3. **No summary** — if neither source is available, no summary message is inserted.

### Activity message insertion anchor

The activity chat message is inserted into `session.Messages` immediately **after**:

1. `group.InitialMessageId` — the first assistant text message that preceded the tool calls, if set; OR
2. `group.TriggeredByUserMessageId` — the user message that triggered this turn, if set; OR
3. The last non-pending, complete user message in the list (fallback).

This three-level priority preserves correct message ordering when the user queues multiple messages
while the agent is already busy processing an earlier one.

---

## Thread Safety

| Lock | Location | Protects |
|---|---|---|
| `ActivityGroupModel._eventsLock` | `ActivityGroupModel` | `Events` list — `AddEvent`, `RemoveEvent`, `GetEventsSnapshot`, and the `Tools` computed property all acquire this lock before touching the list. |
| `ToolExecutionModel._rawEventsLock` | `ToolExecutionModel` | `_rawEvents` list of SDK event JSON blobs. |
| `ToolExecutionModel._childEventsLock` | `ToolExecutionModel` | `_childEvents` list of nested sub-agent / child tool events. |
| `SessionModel.SessionEventLock` | `SessionModel` | Outer lock held by `UnifiedSessionManager` around the entire `Process()` call. Serialises concurrent mutations to `Messages`, `ActiveWorkingGroup`, `StreamingMessages`, and `StreamingThinkingEvents`. |
| `SessionModel.StatusHistoryLock` | `SessionModel` | Guards the `StatusHistory` stack used during permission / user-input request lifecycle. |
| `SessionModel.PendingAttachmentsLock` | `SessionModel` | Guards `PendingAttachments` against concurrent JS-interop paste callbacks and UI-thread file picks. |

> **Note**: `SessionModel.Messages` is **not** intrinsically thread-safe. The `SessionEventLock` in
> `UnifiedSessionManager` is the outer guard. The Blazor renderer reads `MessagesSnapshot`
> (a copy taken under that lock) rather than `Messages` directly to avoid
> concurrent-modification exceptions.

---

## Test Coverage Summary

All tests live under `Tests/Cockpit.UnitTests/Features/SessionEvents/`.

### Handler tests

| Test file | Handler(s) covered | Key scenarios |
|---|---|---|
| `AbortHandlerTests.cs` | `AbortHandler` | Group finalised with Error status; pending messages cleared; "Session aborted" event appended |
| `AssistantMessageDeltaHandlerTests.cs` | `AssistantMessageDeltaHandler` | Streaming message creation; delta accumulation; active-group routing to `StreamingMessages` |
| `AssistantMessageDeltaHandlerAdditionalTests.cs` | `AssistantMessageDeltaHandler` | Live routing to thinking panel; multi-delta accumulation in panel; null-data guard |
| `AssistantMessageHandlerTests.cs` | `AssistantMessageHandler` | Streaming → complete promotion; content override; `StreamingMessages` cleanup |
| `AssistantReasoningHandlerTests.cs` | `AssistantReasoningDeltaHandler`, `AssistantReasoningHandler` | Delta accumulation; complete-event content override; no-group and null-data guards; direct creation when no prior deltas |
| `AssistantTurnStartHandlerTests.cs` | `AssistantTurnStartHandler` | New group creation; `TriggeredByUserMessageId` set; pending message cleared |
| `ImmediateModeReplayTests.cs` | Multiple handlers | Full turn replay ordering; immediate-mode group finalisation |
| `SessionErrorHandlerTests.cs` | `SessionErrorHandler` | Error status set; error message added |
| `SessionErrorHandlerAdditionalTests.cs` | `SessionErrorHandler` | Message type is `Error`; null-data guard; null message falls back to default |
| `SessionEventHelpersTests.cs` | `SessionEventHelpers` | `FindToolExecution` lookup |
| `SessionIdleHandlerTests.cs` | `SessionIdleHandler` | Group finalisation; no-group idle; stuck tools marked error; activity insertion order; multi-turn message ordering |
| `SessionIdleHandlerAdditionalTests.cs` | `SessionIdleHandler` | `PendingTaskSummary` consumed and cleared; summary message content; last-message extraction; `OnSessionFinished` fires; `SuppressFinishedNotification` suppresses it |
| `SessionShutdownHandlerTests.cs` | `SessionShutdownHandler` | Routine shutdown preserves group; routine shutdown with no group leaves status unchanged; error shutdown finalises group; error shutdown with no group sets idle; null-data guard |
| `SessionTaskCompleteHandlerTests.cs` | `SessionTaskCompleteHandler` | Summary stored; null/empty summary not overwritten; summary consumed by idle handler |
| `SessionTitleChangedHandlerTests.cs` | `SessionTitleChangedHandler` | Title updated; null-data guard |
| `SubagentHandlerTests.cs` | `SubagentStartedHandler`, `SubagentCompletedHandler`, `SubagentFailedHandler` | Sub-agent tool lifecycle in group |
| `ToolCompleteHandlerTests.cs` | `ToolCompleteHandler` | Success status update; no-group guard |
| `ToolPartialResultHandlerTests.cs` | `ToolPartialResultHandler` | Partial output appended |
| `ToolProgressHandlerTests.cs` | `ToolProgressHandler` | Progress message updated |
| `ToolStartHandlerTests.cs` | `ToolStartHandler` | New group created; tool added; existing group reused; child nesting under parent; `InitialMessageId` tracked |
| `UserMessageHandlerTests.cs` | `UserMessageHandler` | User message added; `IsPending` set when agent is busy |
