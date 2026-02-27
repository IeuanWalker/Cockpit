using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Cockpit.Components.Controls;

public partial class CodeBlock : ComponentBase
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
	bool _needsHighlight;

	protected override void OnParametersSet()
	{
		if(Code != _prevCode)
		{
			_prevCode = Code;
			_needsHighlight = true;
		}
	}

	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		if(_needsHighlight)
		{
			_needsHighlight = false;
			try
			{
				await _jsRuntime.InvokeVoidAsync("cockpit.highlightBlock", _id);
			}
			catch
			{
				// Ignore if hljs unavailable
			}
		}
	}
}
