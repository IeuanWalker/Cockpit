using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace CopilotUIWebAssembly.Components;

public partial class MarkdownRenderer : ComponentBase
{
	[Parameter] public string Content { get; set; } = string.Empty;
	[Parameter] public string? CssClass { get; set; }

	string _containerId = $"markdown-{Guid.NewGuid():N}";
	string _html = string.Empty;

	protected override void OnParametersSet()
	{
		_html = MarkdownService.ToHtml(Content ?? string.Empty);
	}

	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		try
		{
			await JSRuntime.InvokeVoidAsync("copilotUI.highlightCodeBlocks", _containerId);
			await JSRuntime.InvokeVoidAsync("copilotUI.addCopyButtonsToCodeBlocks", _containerId);
		}
		catch
		{
			// Handle error silently
		}
	}
}
