using Cockpit.Features.Markdown;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Cockpit.Components.Controls;

public partial class MarkdownRenderer : ComponentBase
{
	[Parameter] public string Content { get; set; } = string.Empty;
	[Parameter] public string? CssClass { get; set; }

	[Inject] MarkdownFeature _markdownFeature { get; set; } = default!;
	[Inject] IJSRuntime _jsRuntime { get; set; } = default!;

	readonly string _containerId = $"markdown-{Guid.NewGuid():N}";
	string _html = string.Empty;
	string? _lastContent;
	bool _contentChanged;

	protected override void OnParametersSet()
	{
		string content = Content ?? string.Empty;
		if(content != _lastContent)
		{
			_lastContent = content;
			_html = _markdownFeature.ToHtml(content);
			_contentChanged = true;
		}
	}

	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		if(!firstRender && !_contentChanged)
		{
			return;
		}

		_contentChanged = false;

		try
		{
			await _jsRuntime.InvokeVoidAsync("cockpit.highlightCodeBlocks", _containerId);
			await _jsRuntime.InvokeVoidAsync("cockpit.addCopyButtonsToCodeBlocks", _containerId);
		}
		catch
		{
			// Handle error silently
		}
	}
}
