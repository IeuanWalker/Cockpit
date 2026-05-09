using Cockpit.Features.Sessions;
using Microsoft.AspNetCore.Components;
using SdkPlugin = GitHub.Copilot.SDK.Rpc.Plugin;

namespace Cockpit.Components.Pages.ContextPanel;

public sealed partial class Plugins : ComponentBase, IDisposable
{
	PluginInfoPopup? _pluginInfoPopup;
	readonly SessionListFeature _sessionListFeature;

	public Plugins(SessionListFeature sessionListFeature)
	{
		_sessionListFeature = sessionListFeature;
	}

	List<SdkPlugin> _allPlugins = [];

	int TotalCount => _allPlugins.Count;

	protected override void OnInitialized()
	{
		_sessionListFeature.OnStateChanged += OnStateChanged;
		Refresh();
	}

	void OnStateChanged()
	{
		InvokeAsync(() => { Refresh(); StateHasChanged(); });
	}

	void ShowPluginInfo(SdkPlugin plugin) => _pluginInfoPopup?.Open(_allPlugins, plugin);

	List<SdkPlugin> _renderedPlugins = [];

	protected override bool ShouldRender()
	{
		if(ReferenceEquals(_allPlugins, _renderedPlugins))
		{
			return false;
		}

		_renderedPlugins = _allPlugins;
		return true;
	}

	void Refresh()
	{
		_allPlugins = [.. _sessionListFeature.CurrentSession?.Context.Plugins ?? []];
	}

	public void Dispose()
	{
		_sessionListFeature.OnStateChanged -= OnStateChanged;
	}
}
