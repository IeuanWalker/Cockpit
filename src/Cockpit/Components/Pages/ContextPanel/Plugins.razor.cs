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

	// Tracks the source list reference from SessionContext.Plugins so ShouldRender
	// can skip re-renders when the plugin list has not been replaced by the SDK.
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

	// The SDK always assigns a fresh list to context.Plugins when plugins are loaded
	// (SessionFeature.LoadContextPanelDataAsync replaces the reference, never mutates
	// the existing instance). Tracking list identity is therefore a reliable signal
	// that the plugin set has changed.
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
