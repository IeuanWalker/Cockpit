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
	List<SdkPlugin>? _lastPluginSource;

	int TotalCount => _allPlugins.Count;

	protected override void OnInitialized()
	{
		_sessionListFeature.OnStateChanged += OnStateChanged;
		Refresh();
	}

	async void OnStateChanged()
	{
		await InvokeAsync(() => { Refresh(); StateHasChanged(); });
	}

	void ShowPluginInfo(SdkPlugin plugin) => _pluginInfoPopup?.Open(_allPlugins, plugin);

	protected override bool ShouldRender()
	{
		List<SdkPlugin>? current = _sessionListFeature.CurrentSession?.Context.Plugins;
		if(ReferenceEquals(current, _lastPluginSource))
		{
			return false;
		}
		_lastPluginSource = current;
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
