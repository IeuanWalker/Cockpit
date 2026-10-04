# Terminal workspace tool

This Razor class library owns its terminal renderer, styles, resize handling, screen snapshots and Add to Message dialog. The host implements `ITerminalToolBackend` for local PTY access. `IWorkspaceToolHost.AppendToComposerAsync` supplies chat integration.

Each tool instance owns one shell and one xterm screen. Detaching a Razor component only detaches the screen's element. Output continues to be parsed at the last known size, preserving cursor and alternate-screen state until the tab is shown again. Closing the tool disposes both shell and renderer; closing a running terminal asks for confirmation.

The renderer serializes writes, resize requests, resets and snapshots. ResizeObserver coalesces layout changes to animation frames and only sends changed dimensions. It finishes old writes, resizes xterm, requests the matching PTY size, then consumes shell redraw output. Windows uses xterm's ConPTY compatibility settings. The shell starts only after fonts and the actual panel dimensions are available.

Saved layouts retain terminal contents and dimensions. After restarting the app, saved output is restored and a new shell starts in the saved working directory; running OS processes cannot be resurrected. Switching tabs/sessions within the app keeps the existing shell running.

## Assets and verification

Pinned xterm packages and their licenses are checked into `wwwroot/vendor`. Normal .NET builds require no Node setup or external CDN. To regenerate: `npm ci --ignore-scripts` then `npm run vendor` in this directory.

Run the .NET tests in `Tests/Cockpit.Tools.BuiltIns.Tests` and the Terminal tests in `Tests/Cockpit.UnitTests`. The real-renderer browser checks are in `Tests/Cockpit.JavaScriptTests/terminal.browser.mjs`; run with Node and Playwright available (or set `PLAYWRIGHT_MODULE` to the Playwright module URL). They use headless Edge with a deterministic shell bridge and cover ANSI output, control-key input, repeated resizes, background output, remounting, snapshots and reset isolation.
