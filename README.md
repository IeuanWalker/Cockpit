# GitHub Copilot CLI - Blazor WebAssembly Application

This Blazor WebAssembly application is a conversion of the GitHub Copilot CLI HTML template into a fully functional C# Blazor application.

## Project Structure

### Components

- **SessionsSidebar** (`Components/SessionsSidebar.razor`) - Left sidebar displaying chat sessions with status indicators
- **ChatWindow** (`Components/ChatWindow.razor`) - Main chat interface with message display and input area
- **ContextPanel** (`Components/ContextPanel.razor`) - Right sidebar showing context information (directory, git branch, files, commands, etc.)
- **SettingsPopup** (`Components/SettingsPopup.razor`) - Modal for theme and accent color customization
- **MainLayout** (`Layout/MainLayout.razor`) - Overall application layout

### Services

- **LocalStorageService** - Manages browser LocalStorage operations via JSInterop
- **ThemeService** - Handles theme switching (dark/light) and accent color changes
- **UIStateService** - Manages UI state (sidebar collapse, dropdown states, popup visibility)
- **ChatService** - Manages chat sessions and messages
- **ContextService** - Manages context panel data (files, branches, commands, skills)

### Models

- **ChatSession** - Represents a chat session with messages and metadata
- **ChatMessage** - Represents individual chat messages
- **ContextFile** - Represents files in the context panel
- **SessionStatus** - Enum for session states (Active, AgentRunning, AgentFinished, Archived)
- **FileStatus** - Enum for file modification states (Unmodified, Modified, Added, Deleted)
- **MessageType** - Enum for message types (Text, Code, Typing)

## Features Implemented

### JavaScript to C# Conversions

1. **LocalStorage Management** - Converted to JSInterop service
2. **Theme Switching** - Managed via C# ThemeService with state persistence
3. **Sidebar Collapse/Expand** - Managed via UIStateService
4. **Sidebar Resize** - Implemented via JSInterop with mouse event handlers
5. **Dropdown Toggles** - Managed via UIStateService
6. **Settings Popup** - Component-based with Blazor event handling
7. **Microphone Toggle** - State managed in UIStateService
8. **Auto-resize Textarea** - JSInterop-based auto-height adjustment
9. **Message Sending** - Async C# method with simulated AI response
10. **Auto-scroll Chat** - Automatic scroll to latest messages via JSInterop

### UI Features

- Dark/Light theme support with VSCode-inspired colors
- 12 accent color options
- Collapsible sidebars
- Resizable sidebars (styling ready, resize logic can be enhanced with JSInterop)
- Session management with status indicators
- Chat message display with typing indicators
- Context panel with multiple dropdown sections
- Settings modal

## Running the Application

1. Open the solution in Visual Studio or VS Code
2. Build and run the project:
   ```bash
   dotnet run
   ```
3. Navigate to the URL displayed in the console (typically `https://localhost:5001` or similar)

## Customization

### Adding AI Backend

To connect to an actual AI backend, modify the `SendMessage` method in [ChatWindow.razor](CopilotUIWebAssembly/Components/ChatWindow.razor):

```csharp
private async Task SendMessage()
{
    if (string.IsNullOrWhiteSpace(chatInput))
        return;

    var message = chatInput.Trim();
    chatInput = string.Empty;

    ChatService.AddMessage(message, true);
    ChatService.AddTypingIndicator();

    // Replace this with actual AI API call
    var response = await YourAIService.GetResponseAsync(message, selectedModel);
    
    ChatService.RemoveTypingIndicator();
    ChatService.AddMessage(response, false);
}
```

### Styling

All styles are in [wwwroot/css/app.css](CopilotUIWebAssembly/wwwroot/css/app.css). CSS variables are used for theming, making it easy to customize colors.

### State Persistence

The application uses LocalStorage to persist:
- Theme preference (dark/light)
- Accent color selection
- Sidebar widths (ready for implementation)

## Technologies Used

- **Blazor WebAssembly** - Client-side C# web framework
- **Tailwind CSS** (via CDN) - Utility-first CSS framework
- **JSInterop** - JavaScript interoperability for LocalStorage and DOM manipulation
- **Dependency Injection** - For service management

## Notes

- The application currently uses sample/mock data. Integrate with your backend services for production use.
- Sidebar resize functionality is styled but can be enhanced with mouse event handlers via JSInterop if needed.
- The application is designed to be responsive but optimized for desktop use.

## Next Steps

1. Integrate with AI backend services
2. Add authentication and user management
3. Implement actual file operations
4. Add persistence layer for sessions and messages
5. Enhance mobile responsiveness
6. Add unit tests for services and components
