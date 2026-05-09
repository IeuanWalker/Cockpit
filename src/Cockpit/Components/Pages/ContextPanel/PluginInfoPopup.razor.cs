using Cockpit.Components.Controls;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using SdkPlugin = GitHub.Copilot.SDK.Rpc.Plugin;

namespace Cockpit.Components.Pages.ContextPanel;

public partial class PluginInfoPopup : ComponentBase
{
	PopupBase? _popup;
	SdkPlugin? _selectedPlugin;
	List<SdkPlugin> _plugins = [];
	bool _needsSplitInit;

	readonly IJSRuntime _jsRuntime;

	public PluginInfoPopup(IJSRuntime jsRuntime)
	{
		_jsRuntime = jsRuntime;
	}

	public void Open(IReadOnlyList<SdkPlugin> plugins, SdkPlugin selectedPlugin)
	{
		_plugins = [.. plugins];
		_needsSplitInit = true;
		_popup?.Open();
		SelectPlugin(selectedPlugin);
	}

	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		if(_needsSplitInit)
		{
			_needsSplitInit = false;
			await _jsRuntime.InvokeVoidAsync("cockpit.initializePanelSplit", "plugin-left-panel", "plugin-split-handle");
		}
	}

	void SelectPlugin(SdkPlugin plugin)
	{
		_selectedPlugin = plugin;
		StateHasChanged();
	}
}
