using Cockpit.Components.Controls;
using Microsoft.AspNetCore.Components;
using SdkPlugin = GitHub.Copilot.SDK.Rpc.Plugin;

namespace Cockpit.Components.Pages.ContextPanel;

public partial class PluginInfoPopup : ComponentBase
{
	PopupBase? _popup;
	SdkPlugin? _selectedPlugin;
	List<SdkPlugin> _plugins = [];

	public void Open(IReadOnlyList<SdkPlugin> plugins, SdkPlugin selectedPlugin)
	{
		_plugins = [.. plugins];
		_popup?.Open();
		SelectPlugin(selectedPlugin);
	}

	void SelectPlugin(SdkPlugin plugin)
	{
		_selectedPlugin = plugin;
		StateHasChanged();
	}
}
