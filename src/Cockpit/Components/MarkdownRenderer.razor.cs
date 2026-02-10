using Cockpit.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Cockpit.Components;

public partial class MarkdownRenderer : ComponentBase
{
	[Parameter] public string Content { get; set; } = string.Empty;
	[Parameter] public string? CssClass { get; set; }

	[Inject] MarkdownService MarkdownService { get; set; } = default!;
	[Inject] IJSRuntime JSRuntime { get; set; } = default!;

	readonly string _containerId = $"markdown-{Guid.NewGuid():N}";
	string _html = string.Empty;

	protected override void OnParametersSet()
	{
		_html = MarkdownService.ToHtml(Content ?? string.Empty);
	}

	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		try
		{
			await JSRuntime.InvokeVoidAsync("cockpit.highlightCodeBlocks", _containerId);
			await JSRuntime.InvokeVoidAsync("cockpit.addCopyButtonsToCodeBlocks", _containerId);
		}
		catch
		{
			// Handle error silently
		}
	}
}
