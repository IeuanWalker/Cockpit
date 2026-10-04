# Right sidebar tool workspace — complete implementation plan

## Purpose and document map

Replace the crowded Context panel with a session-owned workspace containing rearrangeable tabs and nested left/right or top/bottom groups. Deliver the complete feature, including all five independent tool libraries, content restoration, settings, and the general embedded browser. This is an implementation plan, not an instruction to begin changing application code.

This document owns the shared contracts, layout, discovery, persistence, settings, and application integration. The following individual plans own each tool's content, state schema, services, extraction, and tests:

| Tool | Independent class library | Implementation plan |
|---|---|---|
| Session context | Cockpit.Tools.SessionContext | [Session context](sidebar-tools/session-context.md) |
| Git changes | Cockpit.Tools.GitChanges | [Git changes](sidebar-tools/git-changes.md) |
| Terminal | Cockpit.Tools.Terminal | [Terminal](sidebar-tools/terminal.md) |
| File explorer | Cockpit.Tools.FileExplorer | [File explorer](sidebar-tools/file-explorer.md) |
| Browser | Cockpit.Tools.Browser | [Browser](sidebar-tools/browser.md) |

Read this shared plan before a tool plan. Work ownership below is for coordination, not separate releases. There are no reduced-scope milestones and Browser is not optional. All completion checks apply to the finished feature.

The user's latest decisions supersede the earlier plan. Screenshots are visual references, not implementation instructions. No previous tab limits, singleton restrictions, layout-only template rules, or Browser-spike exit criteria remain applicable.

## Fixed product decisions

- There is **no product limit on the number of tabs, groups, split nesting, or instances of a tool**. Do not reintroduce a four-group, 32-tab, singleton, or equivalent hidden limit in a reducer, template editor, deserialiser, or terminal backend.
- Every built-in tool supports multiple independently configured instances. Opening Terminal twice creates two distinct terminal identities and, when activated, two distinct shells.
- **Confirmed terminal behaviour:** explicitly closing a running Terminal tab asks for confirmation before terminating that instance. Cancelling keeps it open and running. Changing tabs or sessions keeps the terminal running in the background without a close prompt; returning reattaches the same process and catches up its output.
- The workspace remains in the right-hand region. The existing outer collapse/resize interaction remains; arbitrary floating windows and docking outside this region are not part of this request.
- A tool determines its availability for the particular user session. Its session-bound interface exposes `bool IsAvailable`, evaluated during creation and resume before the catalogue/layout is displayed.
- An unavailable registered tool is absent from the picker and normal tab display. Its saved identity/content can remain dormant internally without displaying a forced unavailable tab. Missing packages cannot evaluate availability and receive a separate recovery treatment.
- Sessions restore the actual configured contents of their individual tabs, not only a list of tool kinds. Default layouts may also include tool-approved starting content.
- Connection status is permanently fixed at the bottom of the right sidebar, outside all tool groups, scrolling content, maximise views, and the empty state. It is not a closable tool. Collapsing the entire sidebar naturally hides its contents.
- The update banner stays in the session list, where the user has already moved it. Do not put it back in the right sidebar.
- Browser is a general embedded browser with its own page tabs, address/search field, Back, Forward, Reload/Stop, and the behaviours specified in its plan. Multiple Browser workspace instances are supported.
- Diagnostics is not a tool in this feature. Do not add a Diagnostics library, catalogue entry, or default tab. Preserve unrelated existing debug functionality without extending this scope.
- Reflection discovers referenced tool libraries at startup without per-tool registration calls. Drop-in DLL installation, trust UI, hot reload, and plugin management are left to the future plugin system.
- Delivery is one completed feature: all five libraries, the common host, settings, restoration, native browser integration, documentation, and tests.

“No limits” means no arbitrary product count or stored-content quota. A computer still has finite memory, disk, native handles, and process capacity. Use lazy activation, virtualisation, streaming, cancellation, and backpressure; report actual allocation/storage failures without truncating the saved workspace. Concurrency and in-memory cache budgets govern work scheduling, not how many items the user may create or restore.

## User interaction and outer layout

The sidebar contains workspace chrome, a flexible tool area, and a fixed connection footer. Use a flex/grid layout with a non-scrolling footer and a `min-height: 0` content region. Native browser surfaces must be clipped above that footer.

Each tab group has an ordered tab strip, active tab, persistent + action, and overflow/search menu. The menu opens a new instance; selecting a catalogue entry never secretly focuses an existing instance. A separate “Go to open tab” command handles that use case. An optional Duplicate action uses the tool's clone contract rather than sharing mutable state or a live process.

Use icons, titles, close buttons, visible focus, and an active-state treatment similar to the references. Tool-generated titles are instance-specific. Distinguish repeated names, such as Terminal 1 / Terminal 2, without using titles as identities. Preserve explicit user titles on session restore.

Support:

- tab reordering within a group and moves between groups;
- splitting left, right, above, and below;
- moving tabs into any existing group;
- nested row/column layouts, including grids larger than 2 × 2;
- resizable dividers, maximise/restore group, reset workspace, and searchable tab/group navigation;
- close active, inactive, or final tab, with tool-provided close confirmation where needed;
- an empty picker view when there are no displayable tabs, while the connection footer remains visible.

Reset workspace is distinct from resetting the user's default. Confirm replacing the current session's arrangement/content, run applicable close guards, instantiate the current default into that session, and persist it. Resetting the default affects future sessions only.

The global `RightSidebarWidth` remains a preferred width. Compute a separate effective width based on the available window. Temporary viewport clamping must not overwrite the user's preferred width. Keep the conversation usable; do not impose a new fixed maximum on the number of splits.

## Identity and ownership

Use stable generated IDs, never array indexes, component instances, filenames, or SDK process IDs:

| Identity | Lifetime and purpose |
|---|---|
| ToolId | Permanent public string, such as cockpit.terminal; identifies a tool implementation |
| RuntimeSessionKey | Opaque in-memory identity of a SessionModel; survives SDK session-ID replacement |
| SessionStorageId | Persisted workspace/profile owner identifier; survives SDK path/ID replacement |
| ToolInstanceId | Persisted identity of one open workspace tab; survives moves and session resume |
| GroupId / NodeId | Persisted layout identity |
| BrowserPageId | Browser-owned persisted identity inside one Browser tool instance |
| IdentityEpoch | Changes when SDK identity/context is rebound; rejects obsolete callbacks |
| ViewGeneration | Changes on view remount; rejects obsolete DOM/native completions |

Terminal resource keys are `(RuntimeSessionKey, ToolInstanceId)`. Browser resource keys additionally include BrowserPageId. There is no session-ID-only terminal dictionary.

There are three distinct tool lifetimes:

1. A startup definition describes and creates tools. An application-singleton definition is not a singleton window.
2. A tool session holds the session-specific availability decision and shared read services for that tool.
3. A tool instance/controller holds one tab's independent state and runtime resources. Its view can be disposed/remounted without closing the logical tab.

The application owns the layout, persistence transactions, navigation, and capability boundaries. Tools own content, state formats, reconstruction, and resources. No tool library may reference the Cockpit executable or persist the shared layout itself.

## Public extension contracts

Create `Cockpit.Tooling.Abstractions`, targeting the supported .NET major version with nullable types, documented public APIs, and compatibility tests. Keep it free of MAUI, SDK SessionModel, executable types, and built-in tool references.

The following signatures define responsibilities; API ownership must freeze consistent names and complete DTOs before dependent implementation. Supply generic base classes so ordinary tools do not reimplement serialization, events, migration dispatch, or no-op lifecycle methods.

~~~csharp
public interface IWorkspaceTool
{
    WorkspaceToolDescriptor Descriptor { get; }

    ValueTask<IWorkspaceToolSession> CreateSessionAsync(
        WorkspaceToolSessionContext context, CancellationToken cancellationToken);

    ValueTask<ToolTemplateExportResult> ExportTemplateAsync(
        ToolStateReadContext source, ToolTemplateExportContext target,
        CancellationToken cancellationToken);

    ValueTask<ToolStateCloneResult> CloneStateAsync(
        ToolStateReadContext source, ToolStateCloneContext target,
        CancellationToken cancellationToken);
}

public interface IWorkspaceToolSession : IAsyncDisposable
{
    bool IsAvailable { get; }
    string? UnavailabilityReason { get; }
    event Action? AvailabilityChanged;

    ValueTask RefreshAvailabilityAsync(
        WorkspaceSessionInfo session, CancellationToken cancellationToken);

    ValueTask<IWorkspaceToolInstance> CreateInstanceAsync(
        WorkspaceToolInstanceContext context,
        CancellationToken cancellationToken);
}

public interface IWorkspaceToolInstance : IAsyncDisposable
{
    WorkspaceToolInstanceId InstanceId { get; }
    event Action<ToolStateChanged> StateChanged;

    ValueTask<ToolRestoreResult> RestoreAsync(
        ToolStateReadContext? savedState, CancellationToken cancellationToken);

    ValueTask<ToolCheckpoint> CaptureAsync(
        ToolStateWriteContext destination, CancellationToken cancellationToken);

    ValueTask OnActivatedAsync(
        WorkspaceToolActivationContext context, CancellationToken cancellationToken);

    ValueTask OnDeactivatedAsync(CancellationToken cancellationToken);
    ValueTask OnLayoutChangedAsync(
        WorkspaceToolLayoutContext context, CancellationToken cancellationToken);

    ValueTask<ToolCloseDecision> CanCloseAsync(
        ToolCloseReason reason, CancellationToken cancellationToken);
}
~~~

### Definition and descriptor

The descriptor contains stable ToolId, API version, display name, icon identifier/fragment, description, state schema version, content descriptor, former tool IDs, and optional template-editor descriptor. It contains **no singleton instance policy or maximum instance count**.

Tool IDs use lower-case reverse-domain names for external tools; `cockpit.*` is reserved for built-ins. Validate identity syntax and alias conflicts independently of reflection order. Persist IDs, not CLR type names. Keep display metadata bounded as metadata validation, not as a limit on tab counts or user document content.

Definitions are cheap, thread-safe factories; constructors perform no I/O and do not obtain session/UI-scoped services. Do not construct content views to validate descriptors. Invalid metadata/constructors are reported per definition.

### Session availability

The factory receives an immutable snapshot of the owning session: user/account capabilities already known to the app, working directory, Git context, platform, SDK state, RuntimeSessionKey, SessionStorageId, and identity epoch. It returns a session-bound object whose IsAvailable is already determined. Do not expose credentials or mutable SessionModel collections.

This is deliberately a boolean **on the session interface**, not a mutable boolean on an application-singleton definition: session A can have Git while session B does not.

Evaluate availability:

- once during new-session preparation, after relevant context is known;
- again on a user-visible resume, including reload after eviction;
- on relevant context/capability changes, explicit Retry, or reconnection if prerequisites changed.

Refresh is cancellable and runs without constructing panes, starting terminals, or navigating browsers. A thrown/failed evaluation is an evaluation error, not a permanent false decision; suppress display temporarily, report it once, retain state, and allow Retry. Do not poll availability on every render.

The host uses IsAvailable as the authority; it does not switch on ToolId to decide eligibility. False removes the tool from the picker and normal display. Show a “Hidden for this session” recovery list in workspace management so saved entries can be inspected, retried, or explicitly removed.

Maintain a canonical saved layout containing dormant entries and a visible projection excluding them. Do not persist the projection over the canonical layout. Active/focused replacement in the projection is transient until the user explicitly changes it. When availability becomes true, the same IDs/content return to their retained positions.

For edits with hidden entries, map visible insertion slots back to canonical IDs: insert before the visible anchor or append after the last canonical tab; never calculate canonical indexes by subtracting visible counts. A branch with dormant tabs is not canonically empty. Splitting/moving visible tabs preserves dormant branches. Templates retain unavailable entries too; they are instantiated dormant without launching work.

For a missing package, no IsAvailable implementation exists. Keep an explicit Missing tool recovery entry (not a fabricated availability result), retain its opaque data, and allow removal. Reinstallation can restore it without another tool's save erasing its contents.

### Per-instance context and host commands

Each instance receives its own IDs, immutable session snapshot, lifetime token, state/checkpoint services, and narrow host capabilities. Never share mutable view state between instances of the same tool.

Commands include ActivateSelf, CloseSelf, OpenNewTool, SetTitle, RequestLayoutNotification, AppendToComposer, and approved reveal/open-file operations. Return typed results such as Applied, NoChange, SessionNotCurrent, InstanceClosed, or InvalidTarget. Appending to the composer rechecks the owning session on the UI dispatcher immediately before mutation; delayed output from A cannot enter B's draft. Tools cannot resolve arbitrary application services through this interface.

The logical instance token survives view remounts. Visibility callbacks receive a separate generation and cancellation token; width/height are measured CSS pixels plus an explicit user-focus-request flag. Hidden or restored inactive panes do not start expensive resources. Every displayed group's active pane is visible, regardless of which group has keyboard focus.

Serialize callbacks per view/controller, cancel visible work immediately on hide, observe asynchronous cleanup, and reject old-generation completions. A cooperative callback timeout fails that view with Retry rather than blocking navigation indefinitely. A hung trusted in-process extension cannot be forcibly sandboxed by these interfaces.

CanClose supports unsaved tool content, terminal-process confirmation, and browser before-unload decisions. Cancellation leaves tab/state/resources intact. After an accepted close, commit removal and release that instance exactly once. Resource shutdown must precede dropping ownership bookkeeping; failed cleanup stays tracked and retryable. Session deletion/shutdown releases all remaining controllers and shared session services.

ToolCloseDecision is Allow, Cancel, or Confirm with a host-rendered message/actions. Coalesce repeated requests and revalidate the instance/version after the user's answer. A browser adapter without a non-destructive native close probe must use an explicit host discard warning, not pretend disposing a web view fires before-unload. Reset/replace collects close decisions before changing the layout; a cancelled decision leaves all original instances intact.

### Content renderers and independently packaged native tools

For Razor tools, the descriptor identifies an IWorkspaceToolComponent with one required public Context parameter. The view binds to its instance/controller. Use DynamicComponent and local error boundaries; durable state is not held only in Razor fields.

Browser needs native embedded web content. Add a public `Cockpit.Tooling.Maui` companion class library containing the generic native-view factory/presenter contract. It references the core abstractions, not the executable. A content descriptor identifies either Razor content or a native surface factory; this is a generic renderer distinction, not a Browser-specific tool switch.

The Browser library implements the native factory and owns its native controls. Cockpit supplies one generic native surface layer over its existing BlazorWebView, with clipping, measured bounds, visibility, focus, occlusion, and disposal. Native surface factories are automatically discovered/validated with their referenced package, or resolved through its descriptor and tool-owned module. Adding a future native tool must not require a new MainPage branch.

Follow the [Browser plan](sidebar-tools/browser.md) for the concrete portal contract and cross-platform proof. The host package owns geometry/placement; Browser owns web engines and page behaviour.

### State and restoration contracts

“Restore” must cover a tab's configured content, view choices, and recoverable user work. It is not just reconstructing its component type.

ToolStateReadContext exposes schema version, detached metadata, logical instance identity, and streaming access to instance-owned content assets. ToolStateWriteContext supplies transaction-staged blob writers and returns opaque asset references. Paths come from the host, never from a serialized arbitrary filename. Tools cannot write shared manifests or another instance's assets.

ToolCheckpoint contains:

- tool schema version and monotonic instance-state version;
- immutable metadata/checkpoint reference and required asset references;
- restoration fidelity/capabilities, including any non-restorable runtime aspects;
- content classification/export information needed by template creation;
- optional recovery diagnostics, without embedding credentials.

Provide generic `WorkspaceToolInstance<TState>` and a tool-owned `JsonTypeInfo<TState>` codec. Stateful libraries own their source-generated JSON context; the host must not enumerate every third-party state type. Stateless tools get an empty codec. Persist data, not arbitrary serialized CLR types, delegates, UI elements, live handles, or DI objects.

RestoreAsync with null creates fresh default content. With stored state, it validates/migrates before activation and returns Restored, Migrated, PartiallyRestored, UnsupportedVersion, Invalid, or NeedsUserInput. Unsupported/failed state stays opaque and is not overwritten by default state. A partial restore explicitly lists preserved content and missing runtime capabilities; it cannot claim exact restoration.

StateChanged carries the instance ID/generation and monotonic version. The controller updates its authoritative in-memory state immediately, even if no view is mounted. Capture produces an immutable checkpoint for a specific version, without starting an unvisited terminal/browser. Components may synchronise small UI measurements before capture, but file I/O does not hold the UI dispatcher or session locks.

Initialize the instance's version counter from the accepted checkpoint; it must not reset below persisted versions after remount. State schema version and instance mutation version are separate fields. Concurrent capture coalesces per instance and cannot publish a checkpoint for another controller generation.

Freeze the remaining result/context semantics with the API:

| Contract | Required contents |
|---|---|
| ToolStateReadContext | Schema/mutation versions, source scope/instance IDs, detached metadata or metadata stream, validated read-only asset access |
| ToolStateWriteContext | Destination scope/instance, staged asset writers, cancellation; no raw shared-manifest path |
| ToolTemplateExportResult | Prepared seed plus included/transformed/excluded report; explicit Partial/Unsupported/Failed outcomes requiring review instead of silent content stripping |
| ToolStateCloneContext | Clone purpose (new session/duplicate), source/destination identities, destination resource bindings, fresh ID mapping and asset ownership writer |
| ToolStateCloneResult | Independent prepared checkpoint or a reasoned deferred/recovery result; never the original mutable controller |
| ToolCheckpoint | Tool schema and mutation versions, coherent immutable content references, restoration-fidelity and save diagnostics |

Built-in tools must implement export/configuration for their declared content. A generic stateless base supplies empty-state export/clone; custom stateful authors can use the typed base for ordinary immutable data and override resource/identity remapping. No public API returns an unrestricted service provider or lets a serialized type select executable code.

For hidden/unavailable/uninstalled tools with no live controller, retain the last checkpoint unchanged. Lazily materialise controllers/state; restoring many metadata records is not permission to launch that many processes or native views.

### Research findings that shape restoration

Persisting process-independent content and reattaching live resources are different operations. The interface therefore distinguishes a durable checkpoint from an in-process resource lease and records restoration fidelity.

| Tool | Required durable content | Warm return | Cold resume |
|---|---|---|---|
| Session context | Section choices, filters, scroll/selection | Same independent view state | Restore view; refresh live session facts |
| Git changes | Repository binding, selected file/hunk, comparison/view settings, cached displayed diff where useful | Same view on shared repository data | Show preserved snapshot while refreshing, and label changed/unavailable source |
| Terminal | Shell configuration, reported working directory, transcript/checkpoint, selection/scroll where practical | Reattach that instance's live PTY | Restore readable previous content; start a clearly labelled fresh shell on visibility when appropriate, never replay old commands |
| File explorer | Root binding, expansion/filter/selection, preview file/content/draft where supported | Restore same independent tree/preview | Restore content and reconcile against disk without silently discarding drafts |
| Browser | Page identities/order, URLs, address drafts, navigation journal/index, zoom/scroll, profile reference | Reattach that page's live native view | Restore pages/history/profile and reload supported resources; disclose non-restored page runtime |

xterm.js provides a serialize addon for the terminal framebuffer, but labels it experimental. Pin and test a compatible serializer; retain a durable transcript fallback rather than treating the framebuffer as a process checkpoint. [xterm.js serialize addon](https://github.com/xtermjs/xterm.js/tree/master/addons/addon-serialize)

Native browser profiles retain cookies/site storage; they do not by themselves define this app's open-page layout or portable tab checkpoints. Keep profile identity separate from tab snapshots. [WebView2 user data](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/user-data-folder), [WKWebsiteDataStore](https://developer.apple.com/documentation/webkit/wkwebsitedatastore)

WKWebView exposes interactionState for restoring interaction in another web view. Treat it as an engine-specific enhancement, not proof of a portable persistent DOM/JavaScript snapshot. The browser plan requires a portable navigation journal and tests engine-specific serialization separately. [Apple interactionState](https://developer.apple.com/documentation/webkit/wkwebview/interactionstate)

The completed implementation must restore all declared content and visibly explain actual limitations: a terminated shell process, a revoked login, a disappeared file, or an arbitrary web app's in-memory JavaScript cannot be promised to return unchanged. These are explicit technical semantics, not excuses to defer the tool.

## Reflection discovery and dependency injection

Application startup contains one host registration:

~~~csharp
builder.Services.AddCockpitWorkspace();
~~~

A referenced tool class library implements IWorkspaceTool and optionally IWorkspaceToolModule. There are no per-tool startup calls, instance-count declarations, or host tool-kind switches.

Use a generated build-time assembly catalogue followed by runtime interface reflection:

1. Inspect resolved managed runtime assets before linking, without executing assemblies. Exclude reference/native/satellite/framework assets. Use actual assembly identities, not NuGet package names.
2. Include assemblies whose reference closure reaches the public tooling contracts, including indirect tool base classes. Generate a deterministic embedded catalogue and preservation inputs.
3. Load packaged identities through the normal application load context; do not rely only on already-loaded assemblies, use arbitrary DLL globs, or load a second abstractions assembly.
4. Reflect externally visible, concrete, closed IWorkspaceTool implementations; respect an ignore attribute. Recover usable types from ReflectionTypeLoadException and report loader errors.
5. Stage an optional public parameterless service module per assembly. Merge tool-owned service descriptors only if its module succeeds; reject replacement of host contracts.
6. Register definition singleton factories, then construct/validate each independently after the provider exists. Freeze the registry before session/template loading. Create session-bound tool objects separately for each session.
7. Keep startup registration idempotent, diagnostics deterministic, and conflicting IDs/aliases invalid rather than choosing whichever reflection returns first.

IWorkspaceToolModule has `void ConfigureServices(IServiceCollection services)`. Modules register package-owned services only and may not build nested providers, start processes, or perform I/O. A throwing module disables that package without leaking staged registrations. A successful module can still supply an invalid service graph; keep provider validation enabled and require composition tests rather than claiming arbitrary DI isolation.

DI owns definition disposal; the workspace owns per-session objects/controllers. Both must be released exactly once. Constructor/component/state failures are local where possible; an in-process class library is trusted code, not a sandbox.

Preserve reflection-reachable components, state codecs, native factories, injected dependencies, and static assets in actual Windows/Mac Catalyst Release builds. Include a publish harness that adds an otherwise untouched sample RCL reference and proves discovery/restoration without a startup edit. Do not change the app's deployment mode to evade the test. [Microsoft trimming guidance](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/incompatibilities)

The sample uses two instances with different content and opposite availability in two fake sessions. Each production tool README must document its own implementation of these same public contracts. Drop-in installation and code-trust management remain outside this feature.

## Layout model, algorithms, and large workspaces

Represent Columns as left/right children with a vertical divider and Rows as top/bottom children with a horizontal divider. Use a binary split tree logically, but store nodes as a **flat ID-indexed collection** so increasing layout depth does not increase JSON object nesting or recursive call-stack depth.

~~~text
WorkspaceState
  RuntimeSessionKey, SessionStorageId, IdentityEpoch
  Revision, LayoutRevision, PersistedRevision
  RootNodeId, FocusedGroupId, MaximisedGroupId?
  Nodes: node ID -> Group or Split
  Instances: instance ID -> ToolId, title, checkpoint reference
  Availability: ToolId -> current session evaluation
  PendingCaptureVersions, PersistenceHealth

Group
  Id, OrderedInstanceIds, ActiveInstanceId?

Split
  Id, Axis, Ratio, FirstNodeId, SecondNodeId
~~~

Use iterative traversal/validation and keyed indexed records. Detect cycles, duplicate IDs, dangling references, multiple parents, and invalid node types without a hard-coded maximum tree depth or node count. A valid empty group is not corrupted data. Do not keep only the first N nodes/tabs, flatten a large saved layout, or impose serializer depth limits that accidentally reject valid layout nesting.

Commands: OpenNewTool, ActivateInstance, CloseInstance, MoveInstance, SplitGroupWithInstance, SetSplitRatio, FocusGroup, MaximiseGroup, RestoreGroups, and ReplaceWorkspace. Prepare IDs/content before atomically committing an open/clone/reset. Return typed failure without a half-created tab or borrowed controller.

Reducer rules:

- pure, deterministic, and free of I/O, tool callbacks, or DOM reads;
- no singleton detection or tab/group quota;
- move preserves instance ID/state; cross-group view remount is allowed;
- insertion index refers to the destination after source removal; moving B in [A,B,C,D] to the end uses index 3;
- canonical groups containing dormant entries are not empty; clean only genuinely empty branches;
- retain one empty root when every instance is intentionally closed;
- state-only changes increment Revision, not LayoutRevision; layout changes increment both;
- no-op/rejected commands produce no revision, persistence, or process side effects.

Virtualise overflow/tab lists, group navigation, and large tool contents. Only visible groups need full UI trees; native views and processes are lazy. Cache eviction can unload idle views, but must checkpoint/reconstruct content and must not terminate live terminal jobs merely because a rendering budget was exceeded.

### Geometry without count restrictions

Use one WorkspaceGeometryPolicy shared by live UI and the default editor. Initial usable leaf footprint is 220 × 160 CSS pixels, including a tab strip, plus a 6 px divider. These are readable viewport dimensions, not restrictions on saved group count.

Calculate subtree minima iteratively: Columns sums child widths and divider and takes the larger height; Rows does the converse. The ratio interval comes from child minima and available extent; do not carry forward an arbitrary 20–80% rule that prevents a large uneven subtree from fitting.

If a saved or newly requested tree cannot fit:

- preserve the full canonical layout and ratios;
- offer a scrollable minimum-size layout surface and a compact focused-group view with searchable group navigation;
- retain a way to create more splits/groups even while compact: a keyboard/menu split creates the structure, focuses the destination, and stays in compact view;
- in a compact drag target, show the proposed structural split and that it will open in compact view; do not disable splitting solely because the viewport is small;
- clamp actual rendered dimensions safely; extreme-size surfaces can be virtualised rather than creating CSS dimensions beyond engine precision;
- automatically return to the fitting projection when appropriate, without saving a rewritten layout.

Preferred outer width remains app-wide. Expand within the available window if useful, but do not make an inability to expand a hidden maximum on layout size. Explicit divider commits persist ratios; temporary render clamping/window resize does not.

### Pointer, keyboard, and native-surface interaction

JavaScript owns pointer previews/measurements; .NET owns canonical changes. Use stable root, group, node, and instance IDs. Every callback carries root registration ID, RuntimeSessionKey/IdentityEpoch or template draft ID, interaction ID, and captured LayoutRevision.

Start dragging after a small movement threshold; exclude close/add/menu controls. Support strip-midpoint reorder, body append, centre move, and four edge split zones. Convert visible targets through the canonical/dormant mapping before commit.

Escape, pointer cancellation, blur, session switch, removal, and stale identity cancel previews. Commit once on drop/release; .NET validates IDs/revision and returns Applied/NoChange/Rejected. Ignore duplicate acknowledgements and restore canonical styles after rejection. A state-only checkpoint must not cancel a drag.

Nested dividers preview locally and commit once. Cancel inner pointer interaction before outer resize. Update both C# sizing and the current JavaScript resize helper; its existing hard-coded range must not conflict with the new policy.

Native browser surfaces are occluded while a drag overlay, modal, or menu would cover them; they cannot swallow workspace drag events. Geometry commits restore native visibility only after clipping/focus state is current.

Each group uses tablist/tab/tabpanel semantics, roving keyboard focus, named overflow controls, and visible focus. Arrows/Home/End navigate the strip; Enter activates. Scope close/cycle shortcuts to workspace chrome, not terminal input, browser content, or editable fields. Provide keyboard/menu equivalents for every drag operation. Dividers expose accessible separator values and keyboard resize/reset. After closing a tab, focus its replacement or the empty group's + action. Cold restore never steals focus from the composer.

## Durable storage and content checkpoints

The earlier single small JSON envelope is insufficient for unrestricted tabs plus terminal transcripts, browser history, or user drafts. Replace it with a versioned manifest referencing immutable per-instance checkpoints/assets. There are no 128 KiB/512 KiB application-content caps or count-based truncation rules.

Proposed storage:

~~~text
<SDK workspace>/Cockpit/Workspace/
  current.json
  generations/<generation-id>/manifest.json
  objects/<content-id>
  workspace.lock

<AppDataDirectory>/Workspace/Sessions/<sha256-SDK-session-id>/
  same structure when no SDK workspace path exists

<AppDataDirectory>/Workspace/Defaults/
  current.json
  generations/<generation-id>/manifest.json
  objects/<content-id>
~~~

The manifest has schema version, SessionStorageId, current SDK identity, flat layout records, active/focus/maximise choices, and per-instance ToolId/state version/checkpoint references. It contains no process handles, arbitrary .NET type names, unvalidated filesystem paths, or raw browser credentials.

Use streamed metadata/content writes and lazy content reads. Physical I/O chunk sizes, cooperative time budgets, and bounded worker concurrency are implementation details; they do not reject legitimate workspaces by item count. Large content is an asset stream rather than an enormous inline JsonElement. Metadata parsing validates record shape and references; tool codecs validate their own data without silently truncating it.

### Capture and commit ordering

Preserve both latest-state correctness and responsive tools:

1. A controller commits its content state and emits a monotonically versioned StateChanged event. Track requested versus checkpointed version per instance; layout changes have their own revision.
2. Coalesce capture requests per instance. Capture outside the shared file-commit gate and outside session/conversation locks. It freezes a coherent state version and stages immutable assets. It must not depend on first mounting a hidden view.
3. Accept a completed checkpoint only if its instance identity/generation is still owned and its version is not older than the accepted version. Closed/deleted instances cannot be resurrected by late capture.
4. Under the single save gate for RuntimeSessionKey, capture the **latest accepted** immutable manifest, revision, and identity epoch. Do not enqueue old pre-captured whole manifests.
5. Write/flush staged objects and a complete new generation. Only then atomically replace the small current pointer with the new generation ID. A crash leaves either the old committed generation or the new complete one.
6. Record durability only for the exact committed layout/checkpoint versions. Changes during capture or I/O schedule another pass. Pending capture or failed content saves must not be reported as “all saved.”
7. Collect abandoned stages/obsolete generations only after they are unreferenced by the current/backup generations, readers, drafts, dormant tools, and in-flight captures. Never garbage-collect missing-package content just because its controller is absent.

Ordinary changes coalesce around 500 ms; continuous terminal output uses a streamed transcript and periodic checkpoints rather than serialising the entire workspace per chunk. A flush captures a target revision/version vector and waits until those changes are durable or a bounded failure result is returned. It need not wait forever for newer streaming output.

A single tool capture failure retains its previous durable checkpoint and visible unsaved status while other layout/content can still save. Do not claim that failed tool content is restored, and do not substitute empty/default content. Retry is local and observable. Initial open/creation failure leaves explicit recoverable state; no false durability guarantee.

Return Saved, AlreadySaved, PartiallySaved, Suppressed, or Failed plus affected instance diagnostics. Use one compact save-health indicator with Retry rather than repetitive toasts. After actual disk/full/permission errors, retain dirty state and retry with bounded backoff, then on change/explicit Retry.

### Load, recovery, identity, and concurrency

Load outcomes are Restored, NotFound, Invalid, UnsupportedVersion, Unavailable, and InUse. Preserve malformed/unknown data before any repair/replacement; if backup fails, keep a transient workspace and suppress writes. Missing or broken per-tool assets produce a recoverable error for that instance, not loss of unrelated tabs.

Use a dedicated OS-released sidecar lease, not a lock on the JSON file being replaced. Another app process can inspect a consistent committed generation but cannot overwrite its active writer. A stale lock pathname alone is not ownership.

Prefer canonical SDK storage; consult fallback only if canonical state is absent. Canonical read/corruption/version errors must not silently select an older fallback. Acquire destination ownership, save the complete generation and all referenced objects, then retire fallback after successful migration.

SDK identity replacement retains RuntimeSessionKey, SessionStorageId, instance IDs, and live resources. Drain writes under the same gate, advance IdentityEpoch, bind the new destination, and force a complete save even if the layout revision did not change. Do not leave content references pointing into a soon-deleted old workspace.

Deletion marks pending, revokes commands/capture completion, drains writers, and coordinates SDK deletion before dropping state. Successful deletion releases every instance and owned asset/profile resource. Failure restores retained state and save eligibility. A late writer must never recreate the deleted workspace.

The earlier design was not shipped, so do not pretend it established a deployed v1 file format. If prototype single-file snapshots are found during implementation, preserve and explicitly import them; never reinterpret their fields silently. Freeze the completed manifest/tool schemas and test real future migrations sequentially.

## Sidebar settings and content-bearing defaults

Add exactly two settings under **Sidebar**:

1. **Open sidebar on new or resumed sessions** — boolean, default true. Applies at successful new-session/cold-resume selection, not immediately when toggled, not on warm switch/reconnect/first-message promotion/model restart.
2. **Default tabs and layout** — compound preview/editor with Edit, Use current workspace, Apply, Cancel, and Reset to Cockpit default.

Use app preferences for the boolean and the app-data Defaults store for the versioned template/content. Do not put a growing content-bearing template into Preferences; Windows has a small per-value storage limit. [MAUI Preferences documentation](https://learn.microsoft.com/en-us/dotnet/maui/platform-integration/storage/preferences?view=net-maui-10.0)

A template contains a flat layout, template-local IDs, active/focused choices, tool identities, and **per-tab seed content with asset references**. Each tool supplies a template editor/configuration view and an export/clone implementation. Repeated tools can have different starting content: two terminal directories, several websites, two explorer roots, or different Git comparisons.

“Use current workspace” asks each tool to export its current checkpoint into the detached template draft. It does not blindly copy a session directory or strip all content. Show a preview/report of included, transformed, and excluded content before Apply:

- include safe configured content such as URLs, queries, view state, approved relative paths, selected file previews, and tool-authored documents;
- rebind session-relative resources to the destination session through explicit path/resource bindings;
- preserve explicitly chosen absolute external bindings as absolute, label them, and validate access on use;
- never copy RuntimeSessionKey, process/native handles, SDK identity, raw login tokens/cookies/passwords, or engine profile files into a template;
- terminal transcripts or other potentially sensitive content can be deliberately included through tool-specific opt-in/export controls, not copied silently;
- a tool that cannot safely export some runtime state must return an explanation while retaining the supported configured content;
- missing packages keep an existing opaque template seed. Importing new unknown session data requires explicit selection with a warning because it cannot be sanitized by its absent tool.

Template configuration must not instantiate operational views: Browser editors do not navigate, Terminal editors do not start shells, and Git/file editors do not mutate repositories. Use separate tool-owned preview/editor components and a no-session template-edit context with limited capabilities. Source checkpoint export is observed/cancellable work; do not execute arbitrary tool startup in Settings.

Apply validates the entire draft and tool exports, stages assets, checks the base content hash, and atomically commits the new template generation. Failure leaves the draft open. Competing edits conflict rather than overwriting a newer template. Back, Cancel, Escape, and popup close share one dirty-discard guard. The current PopupBase requires a guarded native-cancel path; intercept cancel before the dialog closes, not afterward.

Reset removes the user's default pointer/template ownership after confirmation while preserving recoverable unknown data. It does not alter existing sessions. The built-in default is Session context plus Git changes when that tool reports available; no session-specific state leaks into that built-in choice.

New session initialisation captures one committed template generation, generates fresh session/group/instance IDs, invokes each tool's clone contract, and copies/references assets into independently owned session storage before reporting the initial save successful. Shared immutable content-addressed blobs require ownership references; mutable controller/state/profile objects are never shared.

An intentionally empty template creates an empty workspace. A currently unavailable configured tool remains dormant with its seed intact. Missing-tool template entries remain recoverable; do not silently skip their content. Later default edits cannot change an existing session's stored seed.

If the package needed to clone a template seed is absent, preserve an opaque deferred-clone record with source provenance, destination instance identity, and independently owned assets. Do not assume embedded tool-specific IDs were remapped. When the package returns, run its CloneStateAsync for that destination before ordinary RestoreAsync. This keeps unknown data recoverable without accidentally reusing source resources.

## Session lifecycle integration

Use explicit open reasons NewSession, UserColdResume, WarmSwitch, Reconnect, and ModelRestart plus a monotonically increasing user-navigation request ID. NotLoaded alone is insufficient to infer user intent.

| Event | Workspace source and actions | Outer sidebar |
|---|---|---|
| New session | Clone captured default with independent content; evaluate availability; attach; attempt initial durable save | Apply preference when latest selection commits |
| Cold resume with committed state | Restore canonical layout/content references, migrate, reevaluate availability, lazily restore controllers | Apply preference only on successful latest selection |
| Cold resume without saved state | Clone current default and save independent session generation | Same |
| Resume after SDK eviction with newer in-memory state | Keep newer state, refresh session availability, do not replace it with old disk/default data | Apply preference only for actual user cold resume |
| Warm session switch | Schedule outgoing save, show incoming in-memory projection, activate displayed panes only | Preserve user's manual open/closed state |
| Reconnect or SDK first-message promotion | Preserve state/resources; refresh prerequisites if needed | Preserve |
| SDK ID/path replacement | Retain identities/resources; migrate complete content transaction under identity gate | Preserve |
| Unsupported/unreadable/leased store | Preserve original, show transient/recoverable state and suppressed-save status | Apply only if user session selection succeeds |
| Delete/shutdown | Drain/checkpoint, release resources, revoke stale callbacks; honour actual errors | Not a default-layout operation |

Prepare workspace/context before publishing a selected session. The current navigation path publishes the session before asynchronous Git reads finish; change the integration so failed/late work cannot apply another session's sidebar state. Only the latest user request commits current-session and qualifying open preference together on the UI dispatcher.

ToolWorkspace must subscribe directly to relevant session/layout/context changes. MainLayout.ShouldRender cannot be assumed to rerender on every identity change. Add the workspace-specific SessionChangeKind flag without broad conversation-token invalidation. Content state changes notify the owning tool; terminal output does not rerender the full layout.

Eviction checks eligibility, captures/flushes outside SessionEventLock, then rechecks eligibility before releasing SDK data. It retains the small workspace records and does not kill running tool resources. Orderly shutdown makes a bounded best-effort final flush; ordinary saves/checkpoints are the primary durability mechanism. A timeout is reported as unsaved, not success.

## Shared implementation ownership

The packages below describe ownership/dependencies for coordinated implementation, **not delivery milestones**. All are required for one completed feature. Agents should not change shared files opportunistically.

| Owner | Owns | Required handoff |
|---|---|---|
| Public API | Tooling.Abstractions, Tooling.Maui public contracts, generic bases, API tests | Compiling sample with availability, two instances, state/seed round-trip, native factory contract |
| Discovery | Build-time catalogue task/targets, scanner, registry, modules, diagnostics | Untouched referenced RCL discovered in published builds without startup edits |
| Workspace domain | Flat layout records, reducer, canonical/visible projection, geometry, clone IDs | No count/depth/singleton limits; dormant mappings and large/deep tests |
| State/storage | Instance checkpoints/assets, generation store, capture/save coordinator, recovery/migrations | Atomic content + layout restoration; stale capture/write/deletion/identity tests |
| Workspace UI | Tab/groups, pointer/keyboard, compact/virtualised presentation, instance renderer | Independent controllers, guarded close, overflow, focus and native placement contract |
| Sidebar Settings | Defaults store/editor, content export/clone, initialisation precedence | Two-setting UI; independent seed content; dirty-close/conflict handling |
| Each tool owner | Its individual plan and class library, sources/state/native adapters/tests/README | Complete tool with multi-instance, availability, content restoration and default-seed support |
| Application integration | Shared executable files, generic native portal, footer, adapters, lifecycle | All packages wired; obsolete placements removed only after completed replacements pass |

Core API/model contracts come first; storage, host, and libraries implement against those contracts. Integration and cross-platform verification complete the feature. An implementation may sequence commits for reviewability, but no intermediate subset is declared finished.

### Coordinated existing-file edits

| Area | Required integration |
|---|---|
| Cockpit.slnx / Cockpit.csproj | Add five tool libraries, public companion libraries, build task, tests, referenced static assets and linker preservation |
| MauiProgram.cs | One AddCockpitWorkspace host call; generic host adapters/services; reflected tool definitions/modules/factories |
| MainLayout.* | ToolWorkspace replaces ToolPanel; fixed connection footer outside group content |
| MainPage.xaml and code-behind | Generic native surface layer above the app BlazorWebView, clipping/focus/occlusion lifecycle |
| ToolPanel/connection components | Extract connection footer without duplication; update banner remains in SessionsPanel. Retained DebugSection actions remain app-owned behind a debug-only workspace-chrome menu, not a Diagnostics tool or part of the connection footer |
| SettingsSectionEnum / SettingsPopup / PopupBase / dialog.js | Sidebar section, expanded editor, shared close guard, synchronous native cancel interception |
| AppSettingsFeature / UserAppSettings / UIStateFeature | Boolean default, idempotent sidebar-open setter, preferred/effective width separation |
| SessionUiState / SessionStateChange | Workspace ownership and focused notifications |
| SessionFeature creation/loading/navigation/eviction/reconnect/lifecycle | Precedence, latest-navigation commit, availability, capture/save, identity migration, deletion |
| resize.js / index.html | Generic workspace interop, outer geometry input, native occlusion during interactions |
| ChatPanel terminal entry / old TerminalFeature callers | Multi-instance service routing; no old session-only terminal dictionary or duplicate embedded pane |
| EditedFilesRoot / secondary Git window | Thin host for library-owned shared Git view; no dependency cycle |
| Shared file search/ignore helpers | A library-owned/shared policy with host adapter; no file tool reference back to executable |

Each library builds without the executable and owns its UI/state/services/assets. Source interfaces needed to adapt existing Cockpit data are owned by the consuming tool library, implemented by thin executable adapters. Native-specific code belongs in Browser or the generic MAUI companion, not tool-specific branches in the host.

## Verification and complete-feature acceptance

This revision changes documentation only. Establish a fresh code/test baseline before implementation; earlier recorded test counts are not evidence that the future feature works. Use fake clocks and controllable barriers for concurrency tests, not timing sleeps.

Required automated and manual scenarios:

1. Open more than four groups and more than 32 tabs; save/restart and restore every ID, order, active selection, and content.
2. Exercise large and deeply nested flat layouts with iterative validation and virtualised display; no hidden count/depth rejection or truncation.
3. Open multiple instances of every built-in tool in one session with different content; editing one does not change another.
4. Start multiple Terminals concurrently; each instance gets one independent PTY, while repeated activation of the same instance does not duplicate it.
5. Evaluate the same tool as available in A and unavailable in B; only the appropriate picker/tabs display, and dormant content returns when eligible.
6. Resume with all saved tools unavailable, or no tabs at all; show the empty management/picker view and fixed connection footer without losing saved records.
7. Move/split/reorder visible tabs around dormant entries and later restore availability; canonical hidden positions and state remain valid.
8. Preserve a missing package and unknown tool-state version through unrelated saves; reinstall/migrate without data loss.
9. Restore each tool's declared content after full app restart, not merely its type and title; explicitly surface partial/runtime restoration limitations.
10. Make two content-bearing defaults for repeated tools, create A, change defaults, create B, restart, and verify each session retains its captured content.
11. Import the current workspace into defaults with a clear per-tool export report; inspect that live handles, account credentials, and mutable session references were not copied.
12. Apply/Cancel/Reset and dirty-close/conflicting-editor paths never partially overwrite defaults or existing sessions.
13. Queue stale captures/saves and commit newer state; the older completion cannot replace a newer checkpoint or manifest.
14. Delete during in-flight capture/write and replace SDK identity with unchanged layout revision; no resurrection, lost asset references, or orphaned resource keys.
15. Interrupt between asset writes, manifest write, and pointer replacement; load one complete committed generation, not a mixed snapshot.
16. Exercise disk full, denied read/write, failed backup, unknown schema, and concurrent writer; retain old data, show real save status, and support Retry.
17. Restore active terminals/browser pages with the sidebar closed; no expensive engine/process starts until its pane becomes visible.
18. Shrink a large grid into compact/scroll projection, create additional groups there, and expand again; the canonical layout and preferred width survive.
19. Cancel/double-deliver stale drag/resize callbacks across sessions, SDK epochs, and Settings drafts; no wrong-root changes occur.
20. Rapidly select A then B and complete A's delayed Git/state/composer work; A cannot select itself, append to B, or change B's outer preference.
21. Connection status stays at the sidebar bottom through empty state, scrolling, maximise, many groups, and browser overlays; updates remain in the session list.
22. Browser meets its complete plan: multiple outer instances and inner pages, address/search, Back/Forward, reload, navigation recovery, profiles, security, and native embedding on both platforms.
23. Native surfaces respect clipping, popups, drag overlays, keyboard focus, scaling, and session disposal; general websites never gain Cockpit's privileged bridge.
24. Each tool's failure is recoverable locally; capture errors keep prior content and never silently mark it durable.
25. Reference the independent Browser/sample library without adding per-tool startup code; build/publish and prove availability, multiple instances, state restore, and normal static/native assets.
26. Keyboard/screen-reader operations cover tab navigation, overflow, group navigation, split/move/close, and close confirmations without stealing tool-content shortcuts.

Run focused .NET/project tests and JavaScript tests as changes land, then the relevant complete solution suite and platform builds. Do not assume all MAUI target frameworks can be built on one OS. Windows and Mac Catalyst hands-on checks include themes, display scaling, mouse/touchpad, native browser focus, real PTY output, app restart, and actual packaged/Release tool discovery.

### Definition of completed

The feature is complete only when the host, all five independent libraries, settings/default content, per-session restoration, unrestricted multi-instance layouts, session availability, fixed connection footer, automatic discovery, and all applicable acceptance checks are implemented together. Documentation and author examples must match the shipped interfaces. Browser cannot be replaced by an iframe demo, and Terminal cannot retain a session-singleton backend.

Do not claim literal process-memory or arbitrary website-runtime resurrection. Do deliver the specified saved content, honest partial-restore diagnostics, and tested warm-resource continuity. No production code is changed by this planning update.
