# Session context tool — individual implementation plan

## Outcome and dependency

Deliver Session context as `Cockpit.Tools.SessionContext`, an independently building Razor Class Library. It implements the [shared workspace contracts](../right-sidebar-workspace-redesign.md) and is required for the completed feature. Multiple context tabs may show different section/filter/scroll choices in the same session.

This plan owns the context library and its tests. Application integration owns the executable adapter and fixed connection footer. There is no separate partial-release milestone.

## Package boundary and source extraction

Move the current context information into library-owned components:

- Directory and working-directory actions;
- Git branch and changed-files summaries;
- Agents, Instructions, MCP servers, Skills, SDK Plugins, and Allowed commands;
- token-usage information and its existing interactions.

The existing SDK Plugins section is session context, not the future workspace-tool plugin installer. Do not rename its meaning.

ConnectionStatusBanner/history/detail controls remain an application capability, permanently hosted at the bottom of the sidebar outside this tool. The update banner remains in SessionsPanel. Do not copy either into every context instance. Do not create a Diagnostics tool or bring DebugSection into this library; retained developer-only actions move to the integration owner's debug-only workspace-chrome menu.

Proposed files:

~~~text
src/Cockpit.Tools.SessionContext/
  Cockpit.Tools.SessionContext.csproj
  SessionContextWorkspaceTool.cs
  SessionContextToolSession.cs
  SessionContextToolInstance.cs
  SessionContextTool.razor
  SessionContextTemplateEditor.razor
  Sources/ISessionContextToolSource.cs
  Sources/SessionContextSnapshot.cs
  State/SessionContextState.cs
  State/SessionContextJsonContext.cs
  Sections/<migrated section components>
  Components/ControlledExpander.razor
  README.md
Tests/Cockpit.Tools.SessionContext.Tests/
~~~

Reference Tooling.Abstractions and minimal UI dependencies only. Do not reference SessionModel, SessionListFeature, IAppSettingsFeature, or Cockpit.csproj. Extract DTOs consumed by the UI; do not pass executable model objects through an object-typed escape hatch.

ISessionContextToolSource supplies immutable snapshots, session-filtered change subscriptions, and existing supported context actions. Its executable adapter translates existing events; the source is scoped by RuntimeSessionKey and identity epoch. A shared immutable data cache is permitted; each tab's presentation state is separate.

## Availability and instances

SessionContextToolSession exposes IsAvailable = true for an attached Cockpit session whose context can be displayed, including disconnected sessions with cached information. Missing individual context categories do not hide the entire tool. During session creation/resume the factory establishes this value without creating UI; subsequent source changes can refresh it through the public availability contract.

New instance means a new ToolInstanceId and state object. It does not mean a new SDK session or another background fetch for identical data. Never focus/deduplicate an existing context tab when the user chooses + Session context.

Subscriptions route by RuntimeSessionKey and identity epoch. Old results after a model restart or session change must not populate another view.

## Content and state schema

State version 1:

~~~text
SectionVisibility: stable section ID -> visible
SectionExpansion: stable section ID -> expanded
SectionOrder: ordered stable section IDs
FilterText?
SelectedItem: section ID + stable item key?
ScrollAnchor: section ID + stable item key? + offset?
CachedContextReference?
CachedContextIdentity?
~~~

Use stable section/item keys, not localized labels or numeric indexes. Retain unknown section state for forward compatibility. Persist controlled expander values; uncontrolled local IsOpen fields cannot be the only state.

The cached context is an optional last-rendered read model for immediate/offline restoration, with provenance/time. Do not copy API credentials, permission-grant authority, mutable SDK objects, or authentication state. A cached allowed-command list is informational; it cannot authorize an operation after resume.

Restore the instance's section selection/filter/scroll and cached content, then reconcile with current session data. Label cached data until refreshed. A removed selected item produces a local explanation; it must not make the instance fail or erase unrelated preferences. Restoring a scroll anchor waits for the referenced section to render.

Live token usage, branch, connection facts, active agents, and installed SDK extensions must refresh. Do not present an old count as the live session's current value. Capture content changes that materially alter the displayed snapshot through the shared checkpoint contract; coalesce frequent token counters so they do not trigger full-layout writes.

## Default content and cloning

The template editor configures section visibility/order/expansion and filters, and can choose initial selection by stable key. It does not load live tool UI or fetch a future session's data.

Export the current instance's presentation choices and user-authored filter text. By default, bind live facts to the destination session, rather than cloning stale instructions/allowed-command facts as authority. If a read-only cached reference is deliberately included, preserve it as labelled source content, never as the new session's active configuration.

Clone remaps the instance ID, preserves supported presentation content, and creates fresh mutable state. A template with two different context configurations produces two different restored controllers.

## Rendering, actions, and errors

Use the host's measured area, not fixed sidebar dimensions. The library does not own outer collapse, connection-footer placement, native windows, or workspace save files.

Source subscriptions can remain lightweight while hidden, but rendering/scroll work pauses on deactivation. Reattachment reads the latest snapshot and restores presentation state. Do not cancel the logical tab merely because its Razor component moves to a different group.

Existing actions call the narrow source/host interfaces. Any Add to message action is bound to the owning current session and rechecked at commit time. Context information rendered from files is untrusted text/Markdown and must not execute application instructions or arbitrary HTML.

A section load failure shows an error for that section with Retry. Capture/migration errors follow the shared prior-checkpoint preservation rule. Dispose the instance's subscriptions once on close; shared source lifetime is reference-counted and ends at session release.

## Required tests and proof

- Two instances in one session retain different section choices, filters, selection, and scroll through move and restart.
- Shared data refresh reaches both instances without sharing their mutable UI state or adding duplicate watchers per render.
- Availability is established before publication and remains useful for a disconnected session with cached data.
- Cached facts are labelled and cannot grant permissions or replace current session authority.
- Controlled expanders round-trip stable IDs; missing/renamed sections retain compatible state.
- Template export/clone preserves starting presentation while rebinding facts to the new session.
- A stale source result cannot update a replacement SDK identity or another session.
- A hidden/unvisited instance does not force UI creation; moving it preserves content.
- All original context sections and token-usage actions remain reachable.
- Closing all context tabs leaves connection status at the sidebar bottom and the update banner in the session list.
- The RCL builds independently and is discovered from its reference with no per-tool startup registration.

The README includes the definition, session-bound IsAvailable implementation, per-instance state codec, template clone, source adapter contract, and a two-instance example.
