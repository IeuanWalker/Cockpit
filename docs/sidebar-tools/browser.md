# Browser tool — individual implementation plan

## Outcome and dependency

Deliver `Cockpit.Tools.Browser` as a separate, independently building class library demonstrating real workspace extensibility. It implements the [shared workspace contracts](../right-sidebar-workspace-redesign.md) and the public Tooling.Maui native-surface contract.

This is a **general embedded browser**, not an iframe/local-preview spike. Its complete implementation on Windows and Mac Catalyst is required before the overall feature is finished.

There are two intentional levels of tabs:

- outer workspace tabs identify independent Browser tool instances that can be moved/split like other tools;
- inner page tabs belong to one Browser instance and each have their own page/navigation state.

Neither level has a product count limit. Moving an outer Browser tab moves its full page collection. Inner page tabs reorder within that collection; inner-page transfer to another Browser instance is optional only if not needed by the outer workspace requirements. Do not confuse inner page IDs with workspace instance IDs.

## Required browser behaviour

Each instance includes:

- new/close/reorder/select page tabs, stable page titles, active state, overflow/search;
- address/search field, Back, Forward, Reload/Stop, loading/error state, and an origin/security indicator;
- URL navigation, conventional search submission, page links, redirects, and ordinary client-side navigation;
- new-page requests/popups handled through browser-owned page tabs with user-gesture/popup policy;
- normal site login using the native engine's persistent website data, subject to each site's embedded-browser restrictions;
- file upload, explicit download destination/progress/cancellation, ordinary text selection/copy, zoom, and accessible focus;
- safe permission prompts, external-scheme confirmation, native page dialog/before-unload handling, and browser-data controls;
- restoration of page content identity, page ordering, address drafts, navigation journal, supported scroll/zoom, and profile identity.

A fresh Browser tool starts with a blank/new-page view and focused address field only when explicitly opened by the user. Restoring it does not take focus from the composer. Closing the last inner page shows a new-page empty state, not an automatic new outer Browser instance.

Search is submitted only on Enter, not transmitted while typing. Use a packaged default search-provider URL template and allow a provider choice in the browser's own options; this is not a third Sidebar setting. Encode the query safely. Recognize HTTPS URLs and localhost/explicit HTTP development addresses; do not treat an arbitrary scheme as a search-provider command.

## Researched hosting decision

.NET MAUI WebView uses WebView2 on Windows and WKWebView on Mac Catalyst, and exposes navigation/back/forward primitives. Those are the platform engines for this library; verify advanced features through package-owned platform adapters rather than assuming the common control covers them all. [MAUI WebView](https://learn.microsoft.com/en-us/dotnet/maui/user-interface/controls/webview?view=net-maui-10.0)

**Design inference:** a dedicated native browsing surface is the appropriate implementation for unrestricted website navigation inside this Blazor Hybrid app. Keep external content out of the application's privileged BlazorWebView and do not make iframe embedding the general-browser fallback. Hybrid web content and .NET interop cross a trust boundary. [Blazor Hybrid security](https://learn.microsoft.com/en-us/aspnet/core/blazor/hybrid/security/security-considerations?view=aspnetcore-10.0)

Implement a multi-target MAUI class library for the app's Windows/Mac Catalyst targets, with a plain .NET target or separate testable core where needed for non-platform tests. It references Tooling.Abstractions and Tooling.Maui, not Cockpit.csproj.

~~~text
src/Cockpit.Tools.Browser/
  Cockpit.Tools.Browser.csproj
  BrowserWorkspaceTool.cs
  BrowserToolSession.cs
  BrowserToolInstance.cs
  BrowserToolModule.cs
  BrowserSurfaceFactory.cs
  Views/BrowserToolView.cs or .xaml
  Views/BrowserPageStrip.cs or .xaml
  Views/BrowserNavigationBar.cs or .xaml
  Templates/BrowserTemplateEditor.razor
  Engine/IBrowserEngine.cs
  Engine/IBrowserPage.cs
  Engine/BrowserPageController.cs
  Profiles/BrowserProfileService.cs
  Navigation/BrowserNavigationJournal.cs
  Navigation/BrowserNavigationPolicy.cs
  State/BrowserState.cs
  State/BrowserJsonContext.cs
  Platforms/Windows/<WebView2 adapter>
  Platforms/MacCatalyst/<WKWebView adapter>
  README.md
Tests/Cockpit.Tools.Browser.Tests/
~~~

Native controls/page events belong to this library. The executable owns only generic surface placement. Register tool-owned engine factories/services through the optional auto-discovered module. Referencing this package must suffice to discover Browser without AddCockpitBrowserTool or a host Browser-kind switch.

## Generic native-surface integration

The current MainPage hosts the application's BlazorWebView inside a MAUI Grid. The integration owner adds a reusable native surface layer; Browser is its first consumer.

Freeze this public companion contract before implementing either side:

~~~text
IWorkspaceNativeToolViewFactory
  CreateView(instanceContext) -> native View/lease

IWorkspaceNativeSurfaceHost
  Attach(surfaceId, instanceIdentity, nativeView)
  UpdateBounds(surfaceId, bounds, clip, scale, layoutGeneration)
  SetVisibility(surfaceId, visible, occlusionReason)
  RequestFocus(surfaceId, focusIntent)
  Detach(surfaceId, retainContentLease)
~~~

The Browser factory produces its own tab strip, navigation bar, and page content inside the assigned native area. Generic host code does not know browser URLs, history, engines, or page IDs.

The Razor workspace reserves a measured rectangle for the native tool. Geometry callbacks carry root/instance/session identity, SDK epoch, view generation, CSS bounds, clip intersection, and display scale. The host converts CSS coordinates through the WebView/window transform to native units; do not assume device pixels equal CSS pixels.

Required placement rules:

- native surfaces are clipped to the tool body, never cover workspace tab strips, dividers, conversation, or fixed connection footer;
- moving/resizing/scrolling/maximising a group updates or temporarily hides the surface until current bounds are applied;
- hide/occlude surfaces behind app modals, picker/context menus, drag previews, splash layers, and inactive sessions;
- the overlay layer is hit-test transparent outside active native rectangles;
- release pointer capture and route keyboard focus correctly when entering/leaving native content;
- moving a workspace tab detaches/rebinds its view lease but retains the appropriate live browser engines;
- native callbacks after detach/session change are rejected by identity/generation;
- unloading a visual host must not implicitly destroy an unsaved live webpage.

Cross-platform geometry, z-order, accessibility, focus, and occlusion are required implementation proofs, not grounds to replace Browser with an iframe or declare it deferred. If the initial generic portal approach needs platform-specific composition, implement that behind the same public companion contract and record the actual solution.

## Availability, page ownership, and profiles

BrowserToolSession evaluates IsAvailable during creation/resume from supported platform/native-host capability and installed runtime availability. No Git root or SDK internet connection requirement is imposed. An offline site or one failed navigation is a page error, not tool unavailability.

Missing WebView2 runtime yields a recoverable tool availability reason and an explicit install/help path; merely evaluating availability must not silently install software. Retry reevaluates after the environment changes.

Resources are keyed by RuntimeSessionKey + ToolInstanceId + BrowserPageId. A new page is a distinct engine/controller; a new Browser workspace instance is a distinct page collection. Shared engine environments/profiles are permitted, but the mutable navigation/page state is not shared.

Choose one app-local Browser profile by default across Browser instances/sessions, matching familiar browser login continuity. This is a deliberate website-data sharing policy, not shared tab state. Profile ownership is app-level: deleting one session or closing one Browser instance cannot delete website data still used by another.

On Windows configure a dedicated writable Browser user-data folder, separate from the app UI's WebView2 folder. Share a native environment/profile where possible rather than creating an isolated browser process environment for every page. Profiles provide separate site-data stores when additional profiles are explicitly selected. [WebView2 profiles](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/multi-profile-support)

On Mac Catalyst use the platform's persistent WKWebsiteDataStore. Use a named store where supported; the adapter must test API availability against the app's current minimum OS, not silently raise that minimum for profile support. Older supported systems may use the platform default persistent store with strict protected-app-origin navigation blocking and origin-scoped cleanup. Do not promise physical profile partitioning there without platform proof. [WKWebsiteDataStore](https://developer.apple.com/documentation/webkit/wkwebsitedatastore)

Keep profile files/cookies in native managed storage, not JSON snapshots/defaults. A saved profile reference is a local validated logical ID, never a raw arbitrary directory. Browser-data clearing is explicit and origin/profile-aware; do not wipe the trusted app's state or another active profile. Do not copy OS/system-browser credentials into Cockpit.

## State and restoration

State version 1:

~~~text
BrowserToolState
  OrderedBrowserPageIds[]
  ActiveBrowserPageId?
  SearchProviderId
  ProfileReference
  Pages: page ID -> BrowserPageState

BrowserPageState
  TitleFallback
  AddressDraft?
  CurrentCommittedUrl?
  NavigationJournalReference
  CurrentJournalEntryId?
  Zoom
  ScrollAnchor?
  EngineInteractionCheckpointReference?
  EngineCheckpointVersion / platform / provenance
  LastNavigationStatus
~~~

The portable journal records stable entry IDs, URL, display title, navigation relationship/index, and replay classification. Keep native engine entry IDs only as live-generation mappings, not durable identities. Preserve history in streamed assets; do not impose a fixed history/page count as an indirect tab limit.

### Warm navigation

Use native Back/Forward/history while its native entries exist. Observe committed navigations, redirects, same-document changes, and history traversal; reconcile with the journal without duplicating entries or appending when merely going back.

WebView2 can expose navigation metadata through its adapter, including supported DevTools-protocol calls where required; Page.getNavigationHistory returns history entries/current index, while navigateToHistoryEntry addresses an existing engine entry. Those calls do not establish a portable cold-session restore guarantee. [Chrome DevTools Page protocol](https://chromedevtools.github.io/devtools-protocol/tot/Page/#method-getNavigationHistory)

WKWebView exposes interactionState for transfer/restoration into another web view. If the deployed API/binding supports persisting that object safely, version and validate it in the platform adapter; otherwise retain it only as a warm enhancement and use the portable journal on cold restore. Do not use private APIs or assume it serializes arbitrary application memory. [Apple interactionState](https://developer.apple.com/documentation/webkit/wkwebview/interactionstate)

### Cold resume

Restore all page records/order/active selection immediately, with native engines created lazily only for displayed pages. Restore the validated local profile before navigation; do not reset website data on each resume.

If a compatible engine checkpoint can restore native history, use it and verify the resulting journal mapping. Otherwise restore current URL and **host-managed Back/Forward over the portable journal**:

1. Select the requested saved entry without appending a duplicate.
2. If a matching entry exists in the live native engine, traverse to it.
3. Otherwise safely revisit its URL, tag the operation as journal traversal, and update the live mapping after commit.
4. A genuinely new navigation truncates the journal's forward branch just as a normal browser would.
5. Never rebuild history by automatically visiting every old URL on startup.

Revisiting a historical URL is a reload, not resurrection of its old JavaScript/form state. Do not automatically resubmit POST bodies, file uploads, payment actions, blob/data URLs, or entries whose replay safety is unknown. Preserve those entries with an explanation and explicit user action where appropriate. Old native navigation IDs cannot simply be replayed after restart.

Restore zoom and supported scroll after navigation/layout settles without stealing focus. Record a partial-restore diagnostic if page state, login, or engine checkpoint cannot be recovered. The UI must not silently replace a saved page with blank content and call it restored.

Arbitrary DOM/JavaScript heap, cross-origin form contents, revoked credentials, and a site's server state are not guaranteed portable checkpoints. Live hidden page engines should be retained while possible; do not automatically evict pages with uncheckpointable user work solely to satisfy an arbitrary page-count budget. If actual resources run out, retain saved metadata/content and show a local allocation error with recovery.

## Defaults and duplication

Browser template editor lists starting pages, titles/URLs, address drafts where useful, active page, zoom, and search-provider choice. It uses inert configuration UI, not live navigation.

Use current workspace exports the selected page set/configuration and, when explicitly selected, portable browsing history. Show URLs and any sensitive query/fragment warning in the export report. Strip credentials embedded in URL authority; never export native cookies, raw tokens, engine profile directories, or hidden page-runtime secrets.

Clone generates new BrowserPageIds/ToolInstanceId and independent journal ownership. It selects the destination's local approved profile, not a copied source-session credential store. Duplicating an instance/page can share the selected profile intentionally but never a live native view/controller or mutable history list.

A new session seeded with several browser pages starts from those exact page configurations. Closed-sidebar/inactive pages remain inert until displayed, and later template edits cannot change the already-created session.

## Web security and native integrations

External pages never receive the app's Blazor bridge, SDK credentials, generic .NET object proxy, filesystem API, or command execution service. Disable WebView2 host objects/web messages where unused; validate any narrowly required message by origin/schema and never let it invoke workspace commands. [WebView2 security guidance](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/security)

Apply the navigation policy to redirects/new pages as well as typed addresses:

- allow ordinary HTTP/HTTPS browsing, visibly distinguish insecure HTTP, and support explicit localhost development URLs;
- never disable certificate validation, web same-origin protections, or mixed-content checks to make a site load;
- block privileged app origins/asset schemes, arbitrary file:// navigation, and javascript: commands entered in the address field;
- permit non-web schemes only through a clear user-initiated external-app confirmation;
- downloads use a user-approved destination and safe filename handling, show progress/cancel, and never auto-execute;
- uploads require the OS file picker/user selection; sites cannot read arbitrary session files;
- microphone/camera/location/notifications/clipboard requests identify the requesting origin and require native policy/consent; they cannot inherit Cockpit permissions silently;
- popups/new-window requests respect user gesture and origin; popup blocking is not a limit on how many pages the user can open;
- honour native JavaScript dialogs and before-unload with origin-labelled UI and correct workspace modal occlusion.

For Mac Catalyst HTTP/local development support, configure the narrow web-content/local-network policy required by the browser and test ATS behaviour; do not disable transport security app-wide. Record the actual platform configuration in the library's integration README.

Handle native process crashes, certificate/navigation errors, cancelled downloads, revoked permission, and unsupported sites as page-local failures. Some authentication providers reject embedded engines; show that limitation and an explicit Open in system browser action without presenting it as equivalent embedded login state.

## Close and resource lifecycle

Closing an inner page runs the supported native/user-work guard, then closes that page engine and journal ownership. Cancel leaves the page intact. Do not assume native disposal invokes before-unload: WebView2 Controller.Close explicitly does not. Where an adapter cannot probe close non-destructively, use a host confirmation that closing discards unsaved webpage work; do not simulate a trusted browser lifecycle by executing arbitrary page handlers. Honor real native before-unload prompts during navigation where the engine supplies them. [WebView2 close semantics](https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2controller.close)

Closing the outer Browser instance gathers its page close decisions before committing removal. Do not destroy accepted pages piecemeal if another page cancels the overall close. Disposing/remounting a native surface is different from closing the instance; controller leases retain engines when preserving warm page work.

Session deletion/shutdown releases the instance's page engines/download ownership and checkpoints according to shared rules. App-level profiles are retained unless explicitly cleared by the user. Remove engine event handlers and native view references exactly once; one instance's disposal cannot close another's pages.

## Completion proof

- Reference this library with no per-tool startup call; reflection discovers its definition/module/native factory in Debug and actual Release output.
- Build it independently without Cockpit.csproj and document any generic platform-handler integration required by Tooling.Maui.
- Open many outer Browser instances and inner pages with separate navigation state; overflow and virtualisation work without count caps.
- Browse ordinary HTTPS sites, localhost HTTP, redirects, SPA routes, login pages, uploads, and explicit downloads on Windows and Mac Catalyst.
- Verify Back/Forward while warm and after cold restart, including forward-branch truncation and no eager history replay.
- Restore page sets/content identity/address drafts/zoom/profile; label non-restorable page state instead of claiming complete runtime resurrection.
- Test POST/unknown/blob history and prove it is not automatically replayed.
- Show two browser surfaces side by side and in nested rows; resizing/moving/maximising/compact projection preserves correct ownership.
- Ensure footer, app dialogs, menus, and drag previews always appear and receive input above native surfaces.
- Verify focus, keyboard shortcuts, before-unload cancellation, display scaling, clipping, and stale callback rejection.
- Confirm hostile web content cannot access Cockpit's bridge, filesystem, privileged origins, or another tool's context.
- Default export/clone preserves approved starting content without copying credentials or mutable page resources.
- Process/resource/storage failure is local, state remains recoverable, and closing one instance leaves other engines/profile users intact.

This library is the required native-tool extensibility example. Its README must include the reflected definition, session IsAvailable, per-page identity, native factory, checkpoint/template codec, security boundary, and minimal independent host harness.
