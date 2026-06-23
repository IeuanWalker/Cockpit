using Cockpit.Features.Mcp;
using Cockpit.Features.Sessions;
using GitHub.Copilot;
using GitHub.Copilot.Rpc;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Pages.ContextPanel;

public sealed partial class MCPServers : ComponentBase, IDisposable
{
	McpServerInfoPopup? _mcpServerInfoPopup;
	readonly McpFeature _mcpFeature;
	readonly SessionListFeature _sessionListFeature;

	public MCPServers(McpFeature mcpFeature, SessionListFeature sessionListFeature)
	{
		_mcpFeature = mcpFeature;
		_sessionListFeature = sessionListFeature;
	}

	List<McpServer> _allServers = [];
	McpServer? _selectedServer;
	bool _isBusy;

	int TotalCount => _allServers.Count;

	protected override void OnInitialized()
	{
		_sessionListFeature.OnStateChanged += OnStateChanged;
		Refresh();
	}

	void OnStateChanged()
	{
		InvokeAsync(() => { Refresh(); StateHasChanged(); });
	}

	void ShowMcpServerInfo(McpServer server)
	{
		_selectedServer = server;
		_mcpServerInfoPopup?.Open(_allServers, server);
	}

	async Task ToggleServer(McpServer server)
	{
		string? sessionId = _sessionListFeature.CurrentSession?.Id;
		if(_isBusy || sessionId is null)
		{
			return;
		}

		_isBusy = true;
		StateHasChanged();
		try
		{
			bool isEnabled = server.Status != McpServerStatus.Disabled;
			if(isEnabled)
			{
				await _mcpFeature.DisableServerAsync(sessionId, server.Name);
			}
			else
			{
				await _mcpFeature.EnableServerAsync(sessionId, server.Name);
			}
		}
		finally
		{
			_isBusy = false;
			StateHasChanged();
		}
	}

	async Task ReloadServers()
	{
		string? sessionId = _sessionListFeature.CurrentSession?.Id;
		if(_isBusy || sessionId is null)
		{
			return;
		}

		_isBusy = true;
		StateHasChanged();
		try
		{
			await Task.WhenAll(_mcpFeature.ReloadAsync(sessionId), Task.Delay(200));
		}
		finally
		{
			_isBusy = false;
			StateHasChanged();
		}
	}

	List<McpServer> _renderedServers = [];
	McpServer? _renderedSelected;
	bool _renderedIsBusy;

	protected override bool ShouldRender()
	{
		if(ReferenceEquals(_allServers, _renderedServers) && ReferenceEquals(_renderedSelected, _selectedServer) && _isBusy == _renderedIsBusy)
		{
			return false;
		}

		_renderedServers = _allServers;
		_renderedSelected = _selectedServer;
		_renderedIsBusy = _isBusy;
		return true;
	}

	void Refresh()
	{
		string? selectedName = _selectedServer?.Name;
		_allServers = [.. _sessionListFeature.CurrentSession?.Context.McpServers ?? []];
		_selectedServer = selectedName is null ? null : _allServers.FirstOrDefault(s => s.Name == selectedName);
	}

	public void Dispose()
	{
		_sessionListFeature.OnStateChanged -= OnStateChanged;
	}
}