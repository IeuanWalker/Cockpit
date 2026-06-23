using Cockpit.Components.Controls;
using Cockpit.Features.Mcp;
using Cockpit.Features.Sessions;
using Cockpit.Features.Sessions.Models;
using GitHub.Copilot;
using GitHub.Copilot.Rpc;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Cockpit.Components.Pages.ContextPanel;

public partial class McpServerInfoPopup : ComponentBase
{
	PopupBase? _popup;
	McpServer? _selectedServer;
	List<McpServer> _servers = [];
	bool _isBusy;
	string? _sessionId;
	bool _needsSplitInit;

	readonly McpFeature _mcpFeature;
	readonly SessionListFeature _sessionListFeature;
	readonly IJSRuntime _jsRuntime;

	public McpServerInfoPopup(McpFeature mcpFeature, SessionListFeature sessionListFeature, IJSRuntime jsRuntime)
	{
		_mcpFeature = mcpFeature;
		_sessionListFeature = sessionListFeature;
		_jsRuntime = jsRuntime;
	}

	public void Open(IReadOnlyList<McpServer> servers, McpServer selectedServer)
	{
		_servers = [.. servers];
		_sessionId = _sessionListFeature.CurrentSession?.Id;
		_selectedServer = selectedServer;
		_needsSplitInit = true;
		_popup?.Open();
	}

	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		if(_needsSplitInit)
		{
			_needsSplitInit = false;
			await _jsRuntime.InvokeVoidAsync("cockpit.initializePanelSplit", "mcp-left-panel", "mcp-split-handle");
		}
	}

	void SelectServer(McpServer server)
	{
		_selectedServer = server;
	}

	async Task ToggleServer(McpServer server)
	{
		if(_isBusy || _sessionId is null)
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
				await _mcpFeature.DisableServerAsync(_sessionId, server.Name);
			}
			else
			{
				await _mcpFeature.EnableServerAsync(_sessionId, server.Name);
			}

			RefreshFromSession();
		}
		finally
		{
			_isBusy = false;
			StateHasChanged();
		}
	}

	async Task ReloadServers()
	{
		if(_isBusy || _sessionId is null)
		{
			return;
		}

		_isBusy = true;
		StateHasChanged();
		try
		{
			await Task.WhenAll(_mcpFeature.ReloadAsync(_sessionId), Task.Delay(200));
			RefreshFromSession();
		}
		finally
		{
			_isBusy = false;
			StateHasChanged();
		}
	}

	void RefreshFromSession()
	{
		SessionModel? session = _sessionListFeature.Sessions.FirstOrDefault(s => s.Id == _sessionId);
		if(session is null)
		{
			return;
		}

		_servers = [.. session.Context.McpServers];
		_selectedServer = _servers.FirstOrDefault(s => s.Name == _selectedServer?.Name);
	}

	static string GetStatusColor(McpServerStatus status) => McpFeature.GetStatusColor(status);
}
