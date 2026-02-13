# Cockpit - GitHub Copilot GUI

A .NET MAUI Blazor hybrid application that provides a GUI interface for GitHub Copilot.

## Build & Run

```bash
# Restore workloads and build
dotnet workload restore
dotnet build src/Cockpit/Cockpit.csproj

# Run on Windows
dotnet run --project src/Cockpit/Cockpit.csproj --framework net10.0-windows10.0.19041.0

# Run on macOS
dotnet run --project src/Cockpit/Cockpit.csproj --framework net10.0-maccatalyst
```

### Requirements

- .NET SDK 10.0.103 (exact version pinned in global.json with `rollForward: disable`)
- MAUI workload version 10.0.103
- Update workloads: `dotnet workload update --source https://api.nuget.org/v3/index.json`

## Architecture

### Technology Stack

- **UI Framework**: .NET MAUI Blazor Hybrid (Razor components embedded in native MAUI app)
- **Target Platforms**: Windows 10+ (`net10.0-windows10.0.19041.0`), macOS Catalyst 15.0+ (`net10.0-maccatalyst`)
- **Key Dependencies**:
  - `GitHub.Copilot.SDK` - Copilot client integration
  - `CommunityToolkit.Maui` - MAUI extensions (speech-to-text)
  - `Blazor.Sonner` - Toast notifications
  - `Markdig` - Markdown rendering
  - `Microsoft.Extensions.AI.Abstractions` - AI abstractions

### Service Layer

All services registered in `MauiProgram.cs`:

- **Singleton Services** (app-lifetime):
  - `CopilotClientService` - Manages single CopilotClient instance with auto-restart
  - `UnifiedSessionManager` - Orchestrates chat sessions, SDK sessions, and activity tracking
  - `CopilotModelService` - Model selection and configuration
  - `UIStateService` - Global UI state (sidebar collapse, etc.)
  - `TimestampService` - Time formatting utilities
  - `ContextService` - Workspace/file context management
  - `ToastService` - Re-registered as singleton for MAUI Blazor compatibility

- **Scoped Services** (per-component):
  - `ThemeService` - Theme switching
  - `MarkdownService` - Markdown parsing

### State Management Pattern

**UnifiedSessionManager** is the central orchestrator:
- Maintains `List<ChatSession>` for all sessions (persisted + in-memory)
- Tracks single `CurrentSession` visible in UI
- Maps session IDs to SDK `CopilotSession` instances in `_sdkSessions` ConcurrentDictionary
- Handles session lifecycle: create, resume, switch, archive
- Groups tool executions into `ActivityGroup` objects for the "working panel"
- Event-driven updates via `OnStateChanged` event

**ChatSession** model hierarchy:
```
ChatSession
├── Messages (List<ChatMessage>)
├── ActiveWorkingGroup (ActivityGroup?)
├── StreamingMessages (Dictionary<string, ChatMessage>)
└── Model, WorkspacePath, Status, etc.

ActivityGroup (tool execution batching)
├── Events (List<ThinkingEvent>) - chronological messages + tools
├── Status (Running/Complete/Error)
└── Thread-safe event management via Lock

ThinkingEvent (union type)
├── Type: Message | Tool
├── Message (string?)
└── Tool (ToolExecution?)
```

### Key Patterns

1. **Partial Classes for Event Handlers**: `UnifiedSessionManager` split across:
   - `UnifiedSessionManager.cs` - Core session management
   - `UnifiedSessionManager.HandleSessionEvents.cs` - SDK event handlers

2. **Activity Grouping**: Tool executions grouped by proximity:
   - New user message creates fresh `ActivityGroup`
   - Tools/messages batch together until assistant responds
   - Displayed as collapsible "Operations" panel

3. **Streaming Messages**: Messages stream token-by-token, tracked in `StreamingMessages` dictionary by message ID until complete

4. **Session Resumption**: SDK sessions persist via `_sdkSessions` dictionary, allowing UI session switching without recreating SDK state

## Code Conventions

### C# Style (.editorconfig enforced)

- **Tabs for indentation** (4 spaces wide)
- **File-scoped namespaces** (error level)
- **Omit default access modifiers** (error level)
- **Braces required** for all control structures (error level)
- **Field naming**:
  - Private fields: `_camelCase` (suggestion)
  - Instance/static fields: `camelCase` (error)
  - Public fields: `PascalCase` (suggestion)
- **No primary constructors** (suggestion to avoid)
- **Explicit types** over `var` (suggestion)
- **Conditional expressions preferred** for assignments (warning level)

### XAML Style (Settings.XamlStyler)

- Format on save enabled
- Attribute ordering enforced (x:Class, xmlns, dimensions, layout, etc.)
- Remove ending tag for empty elements
- Space before closing slash
- Thickness separator: comma
- No new line for `x:Bind, Binding` markup extensions

### Architecture Conventions

- **DI Constructor Injection**: All services receive dependencies via constructor
- **Event-Driven Updates**: Services raise events; UI subscribes and calls `StateHasChanged()`
- **Async/Await**: All I/O operations use async patterns with CancellationToken forwarding (CA2016 enforced)
- **Null Safety**: Nullable reference types enabled (`<Nullable>enable</Nullable>`)
- **Disposal**: IAsyncDisposable for async cleanup (CopilotClientService)
- **Thread Safety**: ConcurrentDictionary for session maps, Lock for ActivityGroup events

### Project Structure

```
src/Cockpit/
├── Components/
│   ├── Layout/          - MainLayout
│   ├── Pages/           - Routed pages (Home)
│   ├── Popups/          - Modal dialogs (Settings, CreateSession, DeleteSession)
│   ├── Controls/        - Reusable controls (MarkdownRenderer, ToolExecutionDetail)
│   ├── Main.razor       - Main chat interface
│   ├── SessionPannel    - Left sidebar (session list)
│   ├── ContextPanel     - Right sidebar (workspace context)
│   └── WorkingPanel     - Bottom panel (active tool executions)
├── Services/            - Business logic and SDK integration
├── Models/              - ChatSession, ChatMessage, ActivityGroup data models
├── Platforms/           - Platform-specific code (Windows, MacCatalyst)
├── Resources/           - Images, fonts, splash, app icon
└── wwwroot/             - Static web assets
```

## Settings Persistence

`UserAppSettings` uses MAUI `Preferences.Default` for key-value storage:
- Theme (Light/Dark)
- Accent colors
- Send-on-enter behavior
- Sidebar widths

No external storage backend - preferences managed by platform.

## Copilot SDK Integration

- `CopilotClientService.GetClientAsync()` creates/returns singleton client
- Client options: `AutoStart: true, AutoRestart: true, UseStdio: true`
- Sessions created via `client.CreateSessionAsync()` with `SessionOptions` (model, reasoning effort)
- Events: `OnSessionEvent` streams tool calls, messages, thinking, completions
- Session state persisted in `_sdkSessions` dictionary for resumption
