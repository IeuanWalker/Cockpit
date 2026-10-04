# Terminal tool — individual implementation plan

## Outcome and dependency

Deliver `Cockpit.Tools.Terminal` as an independent Razor Class Library implementing the [shared workspace contracts](../right-sidebar-workspace-redesign.md). Every Terminal tab is a separate shell identity with its own directory, output, state, and lifecycle. There is no terminal-per-session limit.

This plan includes backend extraction, multi-instance refactoring, readable content restoration, default-content support, and existing command parity. It is required for the completed feature, not an initial single-terminal version.

## Current-code facts and required changes

The current TerminalFeature dictionary and output event are keyed by SDK session ID. CreateSession spawns a PTY before TryAdd detects an existing session. TerminalSessionModel keeps a 1 MB in-memory tail. The current TerminalPanel lives inside ChatPanel, sizes itself as a 30% panel, and preserves its session shell when its view is disposed.

Do not merely wrap that panel in a new tab. Replace session-only identity throughout the backend, events, tests, and all executable callers before completing integration.

Proposed package:

~~~text
src/Cockpit.Tools.Terminal/
  Cockpit.Tools.Terminal.csproj
  TerminalWorkspaceTool.cs
  TerminalToolSession.cs
  TerminalToolInstance.cs
  TerminalTool.razor
  TerminalTemplateEditor.razor
  TerminalToolModule.cs
  Runtime/ITerminalSessionService.cs
  Runtime/TerminalInstanceKey.cs
  Runtime/TerminalSessionService.cs
  Runtime/TerminalRuntime.cs
  State/TerminalState.cs
  State/TerminalJsonContext.cs
  Persistence/TerminalTranscriptStore.cs
  Shells/<PowerShell and bash integration>
  wwwroot/terminal-interop.js
  README.md
Tests/Cockpit.Tools.Terminal.Tests/
Tests/Cockpit.JavaScriptTests/terminal-workspace.test.mjs
~~~

The library owns Porta.Pty/XtermBlazor dependencies, native process interaction, terminal UI, transcript format, and package assets. It references Tooling.Abstractions, never Cockpit.csproj. Host actions such as Add to message use the public instance context. Import tool JavaScript through its normal RCL asset/module path, not new terminal-private globals in the app's index.html.

## Availability and instance service contract

TerminalToolSession evaluates IsAvailable during session creation/resume using platform shell capability and a valid launch context. A disconnected SDK does not by itself make a local terminal unavailable. Evaluation probes configuration/capability only; it must not spawn a shell.

An invalid directory for one restored tab is a local NeedsUserInput state, not a reason to hide every Terminal instance. Let the user select another directory or choose the destination session directory explicitly.

Suggested backend shape:

~~~text
TerminalInstanceKey = RuntimeSessionKey + ToolInstanceId

EnsureStartedAsync(key, launchConfiguration, size, cancellationToken)
WriteAsync(key, input, cancellationToken)
ResizeAsync(key, size, cancellationToken)
ResetAsync(key, launchConfiguration, cancellationToken)
CloseAsync(key, cancellationToken)
AttachAsync(key, afterSequence, cancellationToken)
CaptureCheckpointAsync(key, cancellationToken)
ReleaseSessionAsync(runtimeSessionKey, cancellationToken)
~~~

Start/reset/close share a **per-instance gate**. Check for an existing live process inside that gate before spawning. Repeated activation of the same tab returns the same runtime; different ToolInstanceIds produce different runtimes. No hidden dictionary keyed only by session ID remains.

Each runtime has process generation, output sequence, parser/transcript checkpoint state, and its own cancellation/subscription ownership. A late read-loop completion from a reset process cannot publish into the new process generation. Removing one key must never close its siblings.

SDK identity/path changes retain RuntimeSessionKey and ToolInstanceId. Resource routing never depends on a mutable SDK ID or an output event's display title.

## Persisted content and fidelity

State version 1:

~~~text
ShellExecutable / shell kind
LaunchArguments: structured arguments, not a composed shell command
LaunchDirectoryBinding: session-relative | explicit user-selected path
LastReportedWorkingDirectory?
WorkingDirectorySource: shell integration | launch fallback
CustomTitle?
TranscriptManifestReference
TerminalFrameCheckpointReference?
CheckpointSequence, ProcessGenerationForHistory
ViewportColumns/Rows? (reconstruction hint only)
ScrollAnchor / selection anchors?
SafePresentationOptions
PreviousRunStatus: never started | running at save | exited
~~~

Use streamed assets for transcript/frame data; do not keep full output in layout JSON or impose the old 1 MB tail as a durable-content truncation rule. A bounded in-memory tail is a cache. Page/archive older output from disk so all successfully stored history remains accessible without loading it all into xterm.

Never persist a process handle/PID as a reconnect authority, command execution queue, raw injected authentication environment, or _isTerminalInitialized flag. Inherited environment is obtained when a new shell starts; it is not copied into defaults/snapshots.

The xterm serialize addon exports framebuffer content and is explicitly marked experimental upstream. Verify compatibility with the xterm version bundled by the chosen XtermBlazor version, pin compatible assets, and include round-trip tests for ANSI/wide characters/modes. Keep the durable transcript as a fallback if a saved frame codec becomes incompatible. [Official serialize addon](https://github.com/xtermjs/xterm.js/tree/master/addons/addon-serialize)

### Working-directory reporting

The current backend records only the launch directory; it does not observe an interactive cd. Add package-owned shell integration for the supported PowerShell and bash launch modes, reporting the current directory through a structured, instance-scoped channel or validated OSC sequence.

Do not overwrite the user's global shell profiles. Install hooks for the launched process, preserve ordinary prompt/profile behaviour, and quote arguments structurally. Validate reported path data; shell output is not an instruction to the host. If integration is unavailable, retain the launch path and label the restoration limitation instead of pretending it is the last interactive directory.

Resolve session-relative bindings against the destination working directory. Explicit external directories are allowed when user-selected and accessible; don't silently force every terminal beneath the repository. A missing/restored inaccessible directory offers a selection/retry state.

### Warm return versus cold resume

Warm tab activation, group moves, sidebar collapse, and session switches reattach the same instance's live PTY. Disposing a view must not terminate its process.

Cold resume restores readable prior output and presentation. On first visibility, create a **fresh** shell in the validated reported/launch directory and insert a clear “Restored previous output — new shell session” boundary. A tab that was saved as exited can remain an exited/read-only history view until Start new shell is chosen.

Do not replay old input as commands, attempt to attach a stale PID, or claim that a running build/TUI survived app termination. The saved framebuffer/transcript is prior content; new shell output is a new process generation. A still-hidden restored terminal must not start merely because its tab is active in a hidden group or the outer sidebar is closed.

Historical replay must be non-executing: suppress terminal query replies to the PTY during replay, disable clipboard-writing control sequences, and do not open links or send input from serialized content. Only fresh user input routes to the newly attached live runtime.

## Output capture and consistency

Assign every decoded output chunk a monotonic sequence number within a process generation. Preserve the existing stateful UTF-8 decoding across partial reads.

Attach obtains an atomic snapshot/high-water mark plus a live subscription. Render the snapshot, then drain later chunks in order. Catch up after hiding using the last rendered sequence, reading older output from the transcript store if the memory cache wrapped. No characters may disappear or duplicate between snapshot, subscribe, replay, and live flow.

A checkpoint must identify its exact output high-water mark. Flush the transcript through that mark, finish parser/frame serialization for the same point, then publish the checkpoint. Output arriving later belongs to the next checkpoint. Shared workspace capture must not scan/copy the entire historical transcript every time another tab moves.

Apply bounded work queues and backpressure to UI writes and transcript I/O. These limits constrain in-flight work, not total stored history. If disk persistence fails, expose a clear “Terminal history not fully saved” status and retain the last durable boundary; never silently discard output and report successful restoration.

Terminal output changes its instance's content version/checkpoint demand, not LayoutRevision. Do not rerender the workspace shell per chunk.

## Close, reset, and session lifecycle

**Confirmed user decision:** ask for confirmation when the user explicitly closes a Terminal tab whose process is running. Changing the active tab or session must keep the process running in the background, without asking to close it. Visibility/deactivation callbacks must not invoke the close guard or stop the process/output reader.

Closing a Terminal tab terminates **that instance's** process after a clear close guard when it is running. Cancel retains the tab, output, and process. This replaces the old keep-one-session-shell-on-tab-close behaviour, which cannot define predictable ownership for unlimited instances.

Treat a live shell waiting at its prompt as running too; do not rely on detecting a foreground command. An exited or never-started terminal needs no running-process confirmation. Use explicit Cancel / Close terminal actions. Recheck process generation after confirmation so an old response cannot terminate a newly reset process.

A new Terminal opened later receives a new ID and new process. Reattaching an existing process means activating/moving the existing tab, not opening a new one.

Reset confirms ending the current process, starts a fresh generation for the same logical tab, and marks the output boundary. Provide a separate explicit Clear history action; neither reset nor resize silently erases archived content.

SDK idle eviction does not kill running shells. Session deletion/shutdown releases all runtimes, including temporarily detached views and cleanup-pending processes. Cleanup errors stay tracked until the OS/runtime confirms exit or the app ends; do not remove the registry entry first and leak the process.

## Defaults and duplication

Template editor configures shell, arguments, title, and directory binding for each Terminal seed. Multiple terminal seeds may target different directories. Restore/start never auto-runs a saved command sequence.

Export current configuration and reported directory. Historical output can be included only through explicit tool-specific selection with a sensitive-content warning; terminal output can contain secrets. If included, export immutable read-only history assets, never runtime process state or environment secrets.

Clone remaps instance/resource identity and asset ownership. A duplicated tab starts a separate shell when visible and may display copied history with a “Copied history” label. It never shares a PTY or writes copied output as input.

## UI parity and tests

The view fills its pane; maximise means workspace-group maximise, not covering the entire app. Fit after activation/layout settles, never at zero size. Preserve reset, selection, copy, Add to message, and keyboard behaviour. Add to message is explicitly initiated by the user and rechecks owning session identity.

Required proof:

- Concurrent starts of one instance spawn one PTY; multiple instances each spawn their own.
- Input/output/reset/resize/close of one instance never affects another.
- SDK ID replacement and view remount preserve the correct runtime key.
- Output during attach/replay/hide/reset is ordered exactly once, including multi-byte UTF-8.
- More than the former 1 MB tail remains recoverably accessible from durable history.
- Cold restart restores content and a clearly new shell, without replaying commands.
- Supported interactive cd reporting restores the directory; unsupported cases are honestly labelled.
- Historical control sequences cannot write the clipboard, open links, or inject PTY input.
- Template/duplicate operations copy approved content but not live processes or secrets.
- Failed capture/storage keeps the last durable history and local unsaved status.
- Closing/deleting one or all terminals cleans up the intended processes; cancelling close changes nothing.
- Switching tabs or sessions while a command produces output shows no close prompt, keeps that same process running, and displays its accumulated output on return. Explicitly closing a live shell prompts even when it is idle at its prompt; an exited terminal closes without that prompt.
- The RCL builds independently, auto-registers, and replaces the old embedded panel only when parity is verified.

Document the instance key, shell integration, transcript schema, checkpoint fidelity, safe clone/export policy, and resource cleanup as the author example.
