# Markdown Support Implementation Plan

## Overview
Add markdown parsing and code syntax highlighting to chat bubbles in the Blazor WebAssembly Copilot UI.

---

## Phase 1: Dependencies

### 1.1 Add Markdig NuGet Package
```sh
dotnet add CopilotUIWebAssembly/CopilotUIWebAssembly.csproj package Markdig
```

### 1.2 Add Highlight.js via CDN
Update `wwwroot/index.html` to include:
- Highlight.js core library
- VS Code Dark theme for consistent styling
- Language support (C#, JavaScript, JSON, etc.)

---

## Phase 2: Core Infrastructure

### 2.1 Create MarkdownService
**File**: `Services/MarkdownService.cs`

**Responsibilities**:
- Parse markdown to HTML using Markdig
- Configure pipeline (code blocks, tables, emphasis, lists, links)
- Sanitize HTML output to prevent XSS attacks
- Handle special characters and escaping

**Methods**:
- `string ToHtml(string markdown)` - Main conversion method

### 2.2 Create MarkdownRenderer Component
**File**: `Components/MarkdownRenderer.razor`
**File**: `Components/MarkdownRenderer.razor.cs`

**Responsibilities**:
- Accept markdown content as `[Parameter]`
- Render sanitized HTML using `MarkupString`
- Apply appropriate CSS classes
- Trigger syntax highlighting after render

**Parameters**:
- `string Content` - Markdown content to render
- `string CssClass` - Optional CSS classes

---

## Phase 3: JavaScript Integration

### 3.1 Extend `wwwroot/js/interop.js`

**New Functions**:
```javascript
// Highlight all code blocks in a container
highlightCodeBlocks(containerId)

// Add copy buttons to code blocks
addCopyButtonsToCodeBlocks()

// Initialize markdown observers
initializeMarkdownObserver()
```

**Features**:
- Auto-detect new code blocks and highlight them
- Add "Copy" button to each code block
- Handle clipboard API for copying code
- Show success feedback on copy

---

## Phase 4: Update Chat Components

### 4.1 Update ChatWindow.razor
**Changes**:
- Replace `<p class="text-sm whitespace-pre-line break-words">@message.Content</p>`
- With `<MarkdownRenderer Content="@message.Content" />`
- Apply to both user messages and Copilot responses

### 4.2 Update ChatWindow.razor.cs
**Changes**:
- Add `highlightNewMessages()` method
- Call after rendering new messages in `OnAfterRenderAsync`
- Ensure highlighting runs after DOM updates

---

## Phase 5: Styling

### 5.1 Update `wwwroot/css/app.css`

**Add Styles For**:

**Markdown Elements**:
- Headings (`h1`-`h6`)
- Paragraphs with proper spacing
- Lists (ordered and unordered)
- Blockquotes with left border
- Horizontal rules
- Tables with borders

**Code Elements**:
- Inline code (`` `code` ``) with background color
- Code blocks with syntax highlighting
- Scrollable overflow for long code
- Copy button positioning and styling
- Line numbers (optional)

**VS Code Theme Integration**:
- Match VS Code dark theme colors
- Consistent background colors
- Proper text contrast

---

## Phase 6: Service Registration

### 6.1 Update Program.cs
**Add**:
```csharp
builder.Services.AddScoped<MarkdownService>();
```

---

## Phase 7: Testing Scenarios

### 7.1 Markdown Features to Test
- ✅ **Inline code**: `` `var x = 10;` ``
- ✅ **Code blocks with language**: ````csharp var x = 10; ````
- ✅ **Bold**: `**bold text**`
- ✅ **Italic**: `*italic text*`
- ✅ **Strikethrough**: `~~strikethrough~~`
- ✅ **Headings**: `# H1`, `## H2`, etc.
- ✅ **Lists**: Ordered and unordered
- ✅ **Tables**: With headers and alignment
- ✅ **Links**: `[text](url)`
- ✅ **Blockquotes**: `> quote`

### 7.2 Edge Cases
- Empty messages
- Very long code blocks (scrolling)
- Mixed markdown and plain text
- Malicious HTML/XSS attempts
- Special characters in code

### 7.3 Performance
- Large messages with multiple code blocks
- Rapid message updates
- Memory leaks from event handlers

---

## Implementation Order

```
Step 1: MarkdownService.cs              ← Core parsing logic
Step 2: MarkdownRenderer.razor(.cs)     ← Reusable component
Step 3: wwwroot/index.html              ← Add Highlight.js CDN
Step 4: wwwroot/js/interop.js           ← Add highlighting functions
Step 5: wwwroot/css/app.css             ← Add markdown styles
Step 6: Program.cs                      ← Register service
Step 7: ChatWindow.razor                ← Integrate MarkdownRenderer
Step 8: ChatWindow.razor.cs             ← Trigger highlighting
Step 9: Testing                         ← Verify all scenarios
```

---

## Technical Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| **Markdown Parser** | Markdig (C#) | Server-side parsing is more secure and performant for WebAssembly |
| **Syntax Highlighter** | Highlight.js | Widely used, supports many languages, VS Code theme available |
| **Sanitization** | Markdig's built-in | Prevents XSS attacks from malicious markdown |
| **Copy Button** | JavaScript | Better UX, clipboard API support |
| **Theme** | VS Code Dark+ | Consistent with VS Code UI |
| **Loading Strategy** | CDN | Smaller bundle size, cached across sites |

---

## File Structure After Implementation

```
CopilotUIWebAssembly/
├── Services/
│   ├── ChatService.cs
│   ├── MarkdownService.cs          ← NEW
│   └── ...
├── Components/
│   ├── ChatWindow.razor            ← MODIFIED
│   ├── ChatWindow.razor.cs         ← MODIFIED
│   ├── MarkdownRenderer.razor      ← NEW
│   └── MarkdownRenderer.razor.cs   ← NEW
├── wwwroot/
│   ├── css/
│   │   └── app.css                 ← MODIFIED
│   ├── js/
│   │   └── interop.js              ← MODIFIED
│   └── index.html                  ← MODIFIED
├── Program.cs                       ← MODIFIED
└── CopilotUIWebAssembly.csproj     ← MODIFIED (Markdig package)
```

---

## Security Considerations

1. **XSS Prevention**: Always sanitize HTML output from Markdig
2. **Link Validation**: Optionally validate URLs before rendering
3. **Content Security Policy**: Ensure CSP allows Highlight.js from CDN
4. **Script Injection**: Never use `eval()` or `innerHTML` directly

---

## Optional Enhancements (Future)

- [ ] Line numbers in code blocks
- [ ] Diff highlighting for code changes
- [ ] Mermaid diagram support
- [ ] LaTeX math rendering
- [ ] Emoji support
- [ ] Markdown preview in input area
- [ ] Export chat as markdown file

---

## Implementation Checklist

### Phase 1: Dependencies
- [ ] Add Markdig NuGet package
- [ ] Add Highlight.js CDN to index.html
- [ ] Add VS Code Dark theme stylesheet

### Phase 2: Core Infrastructure
- [ ] Create `MarkdownService.cs`
- [ ] Create `MarkdownRenderer.razor`
- [ ] Create `MarkdownRenderer.razor.cs`

### Phase 3: JavaScript
- [ ] Add `highlightCodeBlocks()` to interop.js
- [ ] Add `addCopyButtonsToCodeBlocks()` to interop.js
- [ ] Add mutation observer for dynamic content

### Phase 4: Chat Integration
- [ ] Update `ChatWindow.razor` to use MarkdownRenderer
- [ ] Update `ChatWindow.razor.cs` to trigger highlighting
- [ ] Test rendering with sample markdown

### Phase 5: Styling
- [ ] Add markdown element styles to app.css
- [ ] Add code block styles
- [ ] Add copy button styles
- [ ] Test responsive layout

### Phase 6: Registration
- [ ] Register MarkdownService in Program.cs

### Phase 7: Testing
- [ ] Test inline code
- [ ] Test code blocks with syntax highlighting
- [ ] Test all markdown features
- [ ] Test edge cases
- [ ] Test performance with large messages

---

## Sample Test Messages

### Test Message 1: Basic Formatting
```markdown
Here's some **bold text** and *italic text* with `inline code`.

### A Heading

- List item 1
- List item 2
  - Nested item
```

### Test Message 2: Code Block
````markdown
Here's a C# code example:

```csharp
public class MarkdownService
{
    public string ToHtml(string markdown)
    {
        return Markdown.ToHtml(markdown);
    }
}
```
````

### Test Message 3: Mixed Content
```markdown
1. First, install the package:
   ```sh
   dotnet add package Markdig
   ```

2. Then create the service

> **Note**: Remember to register the service in `Program.cs`
```

---

Ready to implement! 🚀
