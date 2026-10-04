# Git changes tool — individual implementation plan

## Outcome and dependency

Deliver `Cockpit.Tools.GitChanges` as an independently building Razor Class Library implementing the [shared workspace contracts](../right-sidebar-workspace-redesign.md). Users can open any number of Git changes tabs, each with its own selection and comparison/view configuration. This complete tool and the existing secondary-window integration are required for the finished feature.

This plan owns the RCL and its tests. The integration owner supplies executable adapters and converts the existing EditedFilesRoot into a thin secondary-window host.

## Extraction and package boundary

Move reusable Git UI, diff parsing/view models, tree UI, styles, and tool-state codecs into the library. Split content from hosting:

~~~text
src/Cockpit.Tools.GitChanges/
  Cockpit.Tools.GitChanges.csproj
  GitChangesWorkspaceTool.cs
  GitChangesToolSession.cs
  GitChangesToolInstance.cs
  GitChangesTool.razor
  GitChangesView.razor
  GitChangesTemplateEditor.razor
  Diff/<shared viewer, parser, models, styles>
  Sources/IGitChangesToolSource.cs
  Sources/GitRepositorySnapshot.cs
  State/GitChangesState.cs
  State/GitChangesJsonContext.cs
  README.md
Tests/Cockpit.Tools.GitChanges.Tests/
~~~

GitChangesView is content only: no 100vh assumption, native window title/theme/splash ownership, session switching, or file persistence. GitChangesTool binds a workspace instance to that view. EditedFilesRoot binds a secondary-window instance to the same view without fabricating a workspace tab context.

IGitChangesToolSource exposes immutable repository identity, changed-file/diff snapshots, context-aware supported comparisons, change events, and existing Git/reveal commands. The executable adapter may call existing Git features, but its DTOs cannot expose SessionModel or mutable GitChangedFileModel collections.

Share repository read caches/watchers by repository/session identity. Do not create a watcher for every tab or reset all tabs' selections on a shared refresh. Mutating operations, if already supported, are serialized at repository ownership and broadcast new immutable results; they are never replayed on state restore.

## Availability and instances

GitChangesToolSession evaluates IsAvailable during creation/resume from the session's Git capability/root. No Git context means false: omit the picker entry and ordinary tabs, retaining canonical dormant instances/content under the shared policy.

A temporarily unreadable repository is a recoverable source error or an unavailable evaluation with reason, depending on whether root identity still exists. An evaluation exception must remain retryable and must not delete content. Reevaluate when session Git context changes.

Every new tab receives a new ToolInstanceId, selection, comparison configuration, expansion set, and controller. There is no singleton/focus-existing rule. Two tabs can select different files and view modes in the same repository.

## State and content

State version 1:

~~~text
RepositoryBinding: destination-session Git root | explicit approved root
RepositoryIdentity: canonical origin/root identity + relevant revision provenance
Comparison: supported comparison kind + base/target refs?
SelectedFile: relative path + previous path? + status identity?
SelectedHunkAnchor?: line/hunk signature + side
TreeView, SplitDiffView, IgnoreWhitespace?
ExpandedDirectoryKeys[]
FileListWidth?
ScrollAnchor?: file/hunk key + offset
FilterText?
CachedListingReference?
CachedDisplayedDiffReference?
CachedDiffProvenance: source commit/blob/worktree fingerprint + timestamp
~~~

Only expose comparisons already implemented or explicitly implemented within this tool; do not create a decorative comparison setting that the backend ignores.

Use source-generated metadata and streamed assets for cached display content. Capture the actual selected diff and listing needed to reconstruct the user's visible tab; do not reduce restoration to SelectedRelativePath alone. Avoid snapshotting the entire repository or every file's diff merely because the tree lists them.

A cached diff is a labelled read-only checkpoint, not the authoritative worktree/index. Never restore a cached patch by applying it to Git. Path validation is separator-aware and bound to the selected repository; stale paths cannot escape through traversal or follow unapproved links.

On resume:

1. Restore view configuration, selection, and cached display content without a UI startup dependency.
2. Show the checkpoint with its provenance/stale indicator while refreshing current Git data.
3. Resolve the file by path/status/rename information and hunk by a stable signature where possible.
4. If the file/hunk no longer exists, retain the old selection/content as a recoverable “changed since saved” view with an explicit action to choose current data. Do not silently show a different first file and claim exact restore.
5. Rebind session-relative repository references to the current session; if repository identity differs, explain the mismatch before treating old content as current.

For a fresh instance, seed applicable view preferences from an immutable host-provided initial-preferences DTO. After it has saved state, that instance's state wins over global defaults and other tabs.

## Defaults and duplicate content

The tool template editor supports per-tab view mode, filter, supported comparison, repository binding, and initial selected relative path. It may include a cached selected diff as an explicit read-only reference, clearly labelled with its source repository.

Export current content/configuration through ExportTemplateAsync. Do not copy a current session's repository object, watcher, staged operation, raw absolute workspace identity, or mutation queue. Present session-relative versus explicit fixed-root bindings clearly.

Clone creates fresh instance state and independent asset ownership. A duplicate can copy current read-only display/configuration, but never shares mutable selection or invokes a Git write. New sessions with temporarily unavailable Git keep their configured instances dormant until the tool reports available.

## Secondary-window behaviour

Retain the current Git pop-out capability using GitChangesView. A pop-out remains bound to the source session/repository, not whichever session later becomes current.

The source workspace instance supplies initial view content if the user pops out that instance. The pop-out then owns independent presentation state unless an explicit synchronized-view feature is separately designed. Closing either host leaves the other usable. Both share repository data and command serialization, not UI state.

Keep secondary-window sizing/theme/splash logic in Cockpit's thin host. It must not register a second Git tool implementation or introduce an RCL-to-executable dependency.

## Lifecycle and errors

Lazy view mounting cannot perform Git mutations. Deactivation stops render/scroll work; shared source updates can update the instance model without rerendering the entire workspace. Reactivation resolves current data and restores scroll after measuring a non-zero viewport.

Capture produces a coherent listing/diff/provenance checkpoint for one instance version, never a mixture of refresh generations. Late repository results carry RuntimeSessionKey, identity epoch, and repository generation. Dispose subscriptions on instance/session release, with ref-counted watcher cleanup.

Missing files, invalid refs, permission failures, and unsupported state versions have local recovery states. Preserve prior checkpoints after migration/capture failures; Retry must not erase cached user-visible content.

## Required tests and proof

- Several tabs retain different files/hunks, filters, and split/tree modes through moves, warm switches, and cold restart.
- Multiple instances reuse read/watch services without sharing mutable view state.
- Git availability differs between sessions and dormant content returns when a root becomes available.
- Cached content is restored and labelled before refresh; changed/deleted/renamed files do not silently select unrelated content.
- Repository-root changes reject stale relative-path assumptions.
- State restoration/default cloning never stages, applies, commits, or discards a patch.
- Template export copies approved configured content with independent IDs/assets and destination-relative bindings.
- One coherent checkpoint survives refresh/capture races and failed writes.
- Workspace and secondary-window hosts can open/close independently.
- Existing Git/diff tests remain valid after extraction; the RCL compiles without the executable and auto-registers from its reference.

The README demonstrates the session availability decision, two independent selections over shared repository data, content checkpoint/clone implementation, and executable source adapter.
