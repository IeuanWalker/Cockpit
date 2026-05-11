using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Cockpit.Components.Controls;

public sealed partial class CodeBlock : ComponentBase
{
	[Parameter] public string Code { get; set; } = string.Empty;
	[Parameter] public string Language { get; set; } = "plaintext";

	readonly IJSRuntime _jsRuntime;

	public CodeBlock(IJSRuntime jsRuntime)
	{
		_jsRuntime = jsRuntime;
	}

	readonly string _id = $"cb-{Guid.NewGuid():N}";
	string _prevCode = string.Empty;
	string _prevLanguage = "plaintext";
	bool _renderPending;
	bool _needsHighlight;

	protected override bool ShouldRender() => _renderPending;

	protected override void OnParametersSet()
	{
		if(Code == _prevCode && Language == _prevLanguage)
		{
			return;
		}

		_prevCode = Code;
		_prevLanguage = Language;
		_renderPending = true;
		_needsHighlight = true;
	}

	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		_renderPending = false;

		if(!firstRender && !_needsHighlight)
		{
			return;
		}

		_needsHighlight = false;
		try
		{
			await _jsRuntime.InvokeVoidAsync("cockpit.highlightBlock", _id);
		}
		catch(JSException)
		{
			// hljs not yet loaded or JS function unavailable — expected on first render
		}
		catch(InvalidOperationException)
		{
			// WebView being torn down — safe to ignore
		}
	}
}
