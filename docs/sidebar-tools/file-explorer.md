# File explorer tool — individual implementation plan

## Outcome and dependency

Deliver `Cockpit.Tools.FileExplorer` as an independent Razor Class Library implementing the [shared workspace contracts](../right-sidebar-workspace-redesign.md). Users can open multiple file explorers with different roots, filters, expansion state, selections, and read-only file previews.

The tool is complete when browsing, preview content, restoration, default seeds, path safety, and host actions work together. A text editor is not implied by this request; do not add unplanned editing/save semantics or leave Open file as a non-functional button.

## Package and source ownership

The current Directory context section opens the OS explorer; it is not this persistent tree. Existing FileSearchFeature is a search/index service, not a ready-made lazy explorer. Extract/share its ignore policy, not its eager whole-repository enumeration.

~~~text
src/Cockpit.Tools.FileExplorer/
  Cockpit.Tools.FileExplorer.csproj
  FileExplorerWorkspaceTool.cs
  FileExplorerToolSession.cs
  FileExplorerToolInstance.cs
  FileExplorerTool.razor
  FileExplorerTemplateEditor.razor
  FileExplorerToolModule.cs
  Files/IWorkspaceDirectoryService.cs
  Files/WorkspaceDirectoryService.cs
  Files/IWorkspaceIgnorePolicy.cs
  Files/WorkspacePathResolver.cs
  Preview/FilePreview.razor
  Preview/FilePreviewService.cs
  State/FileExplorerState.cs
  State/FileExplorerJsonContext.cs
  README.md
Tests/Cockpit.Tools.FileExplorer.Tests/
~~~

The library owns the directory/read-only preview services, UI, states, and any package-specific module. It references Tooling.Abstractions, not the executable. A small shared policy library or tool-owned interface allows Cockpit's search adapter and explorer to use the same ignore rules without a dependency cycle.

Root selection/reveal/open-with-system-app actions use public host capabilities with the owning instance/session identity. The tool may enumerate/read an explicitly selected root; it must not obtain an unrestricted IServiceProvider to access unrelated application features.

## Availability and independent instances

FileExplorerToolSession evaluates IsAvailable during creation/resume from filesystem browsing capability. A session with no working directory can still show the tool if the user is allowed to choose a root. An inaccessible stored root is a per-instance NeedsUserInput/error state, not a reason to hide every explorer.

Each new tab has its own root binding, tree/filter state, selected file, and preview state. There is no singleton lookup. A shared immutable directory cache is allowed, keyed by validated root identity and filesystem generation; mutable expansions/selections are never shared between instances.

Availability changes use the shared dormant projection. Restore does not create filesystem watchers or enumerate every saved expansion before determining whether the tool may display.

## Required UI

Provide a root breadcrumb/selector, filter, refresh, expandable directory tree, selected-file read-only preview, and actions for Reveal, Copy path, and Add to message.

Opening a text file displays a plain-text/code preview within the tool. Markdown can have a sanitized read-only rendering toggle; raw HTML/script content is never executed. Binary/unsupported formats show metadata with safe Reveal/Open in system app rather than trying to decode arbitrary bytes as text.

Large directories/files use incremental loading and virtualisation. Navigation and cancellation remain available while work runs. Multiple explorer tabs can compare different files or roots side by side. Preview divider/selection belongs to that instance.

If an existing general file viewer is integrated later within this implementation, use a documented host capability; do not assume a viewer exists and defer Open indefinitely. The contained read-only preview is the required baseline behaviour.

## State, content, and file reconciliation

State version 1:

~~~text
RootBinding: session-relative | explicit selected root
RootIdentity / source provenance
ExpandedRelativePaths[]
SelectedRelativePath?
FilterText, ShowHidden? / ignore-view choice
TreeScrollAnchor, PreviewScrollAnchor
PreviewSelection?
PreviewMode: text | sanitized Markdown | metadata
TreePreviewDividerRatio
PreviewFileFingerprint?
CachedPreviewReference?
CachedDirectoryListingReference?
~~~

All expanded paths are persisted; the old “restore at most 100 directories” rule is removed. Schedule expansion/metadata reads lazily with bounded concurrency and cancellation, but keep pending saved paths until they can be realized. Work budgeting must not delete unprocessed expansion state.

Cached preview stores the content/ranges actually loaded for display, plus encoding/fingerprint/provenance, in streamed tool assets. It is not a backup of every file in the root. Large previews can preserve opened ranges and resume paging from disk. Preserve an entire small opened document when it was fully loaded. Do not reduce a content-bearing checkpoint to a pathname alone.

Cold restore:

1. Restore root configuration, expansion/filter/selection, and cached displayed content into an inert controller.
2. Validate/rebind the root when the tool is eligible/visible.
3. Show cached preview with a “Saved preview” indicator until the current file is checked.
4. Compare a robust fingerprint appropriate to the read path; if content changed/disappeared, preserve the saved preview and offer Reload current file / choose another file.
5. Realize expanded ancestors before children as they are displayed; restore scroll/selection after the required nodes/content exist.

A saved preview is never written back to the filesystem automatically. There is no restore-time permission to recreate a missing file, overwrite changes, or apply a cached draft. If future editing is added, it requires a separate dirty-document/close/conflict implementation; this tool does not pretend to have it.

## Path and enumeration safety

Use structured root bindings and normalized relative paths, not arbitrary absolute paths embedded in child requests.

- Resolve child paths against the currently validated root with separator-aware containment and platform-correct case rules.
- Reject traversal/rooted child paths and stale root-generation requests.
- Handle symbolic links/junctions as explicit entries; do not recursively traverse their targets implicitly. An explicit Open target action establishes and validates a new root binding.
- Account for links in root/ancestor resolution, not only lexical path prefixes. Prefer handle/canonical-target validation where available; reject ambiguous traversal when it cannot be validated.
- File rename, access denial, removal, loops, and refresh races become local node states with Retry.
- Enumerate direct children of requested expanded nodes off the UI thread; do not eagerly walk the repository.
- Sort directories before files with stable deterministic ordering, retaining actual filename casing.
- Apply shared ignore rules consistently, with supported user display options rather than inventing ignored entries from the reference screenshots.
- Tag each query/result with RuntimeSessionKey, ToolInstanceId, identity epoch, root identity, and generation. A late result from another root cannot populate the current tree.
- Treat preview contents, filenames, Markdown, and links as untrusted data. No script execution or automatic external URL launches.

Physical batches may be small for responsiveness, but every child remains reachable through virtualisation/Load more. There is no product cap on directory count, selected root size, or persisted expansion count. Actual disk/memory/access errors are surfaced honestly.

## Default seeds and duplication

Template editor configures a root binding, filter, initial expanded/selected paths, and preview configuration for each seed. Optional cached starting preview is content with provenance, not authority to write a file.

Use current workspace exports the current selected/opened preview and view state subject to the export report. Rebind session-relative paths to the destination; keep explicitly selected external roots explicit and visible. Do not accidentally store a whole source session's absolute workspace directory as an invisible dependency.

Clone/duplicate remaps instance IDs and mutable expansion/filter/selection objects. Immutable copied preview assets gain independent ownership through the shared store. Two seeded explorers must not share mutable tree nodes or watchers whose callbacks target only one instance.

## Lifecycle and tests

Capture does not require a rendered tree. The controller retains its authoritative view/content checkpoint while the view is hidden or remounted. Pause enumeration not needed by an inactive view; resumed demand continues from saved pending expansions instead of starting a new full walk.

Close/disposal cancels that instance's reads and releases its subscriptions. Shared read/watch services use explicit leases; closing one explorer cannot cancel another's work.

Required proof:

- Two explorers with different roots/files/filters restore their own displayed content and state.
- A large expansion set well beyond 100 entries survives save/restart without truncation.
- Large directories and previews remain navigable without eagerly allocating all content.
- Cached preview survives file change/deletion and is labelled; restore never overwrites disk.
- All child/root/link cases obey validated containment and explicit root-change rules.
- Rapid root/session changes reject stale enumeration/preview results.
- Availability differs appropriately by session capability without confusing one bad root with all-tool unavailability.
- Template export/clone produces independent content and correct destination-relative bindings.
- Add to message cannot append to another session after a delayed file read.
- Unsupported/failed migration retains the old checkpoint; capture/storage errors leave Retry.
- The independent RCL and shared ignore policy have no executable reference and auto-register from the project/package reference.

The README shows the session availability interface, independent tree/controller identity, root-binding codec, cached content restore, and safe template export.
