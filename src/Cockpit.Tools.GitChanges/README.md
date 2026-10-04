# Cockpit Git Changes tool

This independently building Razor Class Library owns immutable Git display DTOs, diff parsing, per-tab controllers and restorable state. It does not reference the Cockpit executable, `SessionModel`, native windows, application settings, or mutable Git collections.

The executable supplies one `IGitChangesToolSource` adapter. That adapter may share repository caches and watchers, while each `GitChangesToolInstance` keeps independent selection, filter, tree expansion and diff mode. `GitChangesToolSession.IsAvailable` is evaluated for each Cockpit session before publication and can change without deleting dormant state.

Checkpoints stream the selected diff as an asset and keep the changed-file listing as immutable metadata. Restored content is explicitly labelled read-only until current repository identity and content are reconciled. Restore and clone never run Git mutations.

`GitChangesView` contains no native-window or session-selection policy. The existing secondary window can host this component with an independently-created controller over the same source.
