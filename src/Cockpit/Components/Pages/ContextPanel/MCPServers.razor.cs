using Cockpit.Features.Sessions;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Pages.ContextPanel;

public partial class MCPServers : ComponentBase, IDisposable
{
	readonly SessionListFeature _sessionListFeature;
	public MCPServers(SessionListFeature sessionListFeature)
	{
		_sessionListFeature = sessionListFeature;
	}

	string McpServerUrl => _sessionListFeature.CurrentSession?.Context?.McpServerUrl ?? string.Empty;
	bool McpServerConnected => _sessionListFeature.CurrentSession?.Context?.McpServerConnected ?? false;

	protected override void OnInitialized()
	{
		_sessionListFeature.OnStateChanged += OnStateChanged;
	}

	void OnStateChanged()
	{
		InvokeAsync(StateHasChanged);
	}

	public void Dispose()
	{
		Dispose(true);
		GC.SuppressFinalize(this);
	}

	protected virtual void Dispose(bool disposing)
	{
		if(disposing)
		{
			_sessionListFeature.OnStateChanged -= OnStateChanged;
		}
	}
}