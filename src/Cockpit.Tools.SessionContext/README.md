# Cockpit Session Context tool

This Razor Class Library owns session-context presentation, immutable DTOs, and per-tab state. It has no dependency on the Cockpit executable or its mutable session models.

The executable supplies `ISessionContextToolSource`, scoped by runtime-session key and identity epoch. Multiple `SessionContextToolInstance` objects may subscribe to the same source/cache while retaining independent filters, selection, section order, expansion, and scroll anchors. Cached facts are display-only and are always labelled until refreshed; they never grant permissions.

`SessionContextToolSession.IsAvailable` is evaluated before the session is exposed to the workspace. A disconnected session can remain available when cached context is displayable. Connection status and update status deliberately remain outside this library.
