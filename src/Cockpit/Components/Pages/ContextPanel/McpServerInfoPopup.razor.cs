using Cockpit.Components.Controls;
using Cockpit.Features.Mcp;
using Cockpit.Features.Sessions;
using Cockpit.Features.Sessions.Models;
using GitHub.Copilot.SDK.Rpc;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Pages.ContextPanel;

public partial class McpServerInfoPopup : ComponentBase
{
	PopupBase? _popup;
	McpServer? _selectedServer;
	List<McpServer> _servers = [];
	bool _isBusy;
	string? _sessionId;

	readonly McpFeature _mcpFeature;
	readonly SessionListFeature _sessionListFeature;

	public McpServerInfoPopup(McpFeature mcpFeature, SessionListFeature sessionListFeature)
	{
		_mcpFeature = mcpFeature;
		_sessionListFeature = sessionListFeature;
	}

	public void Open(IReadOnlyList<McpServer> servers, McpServer selectedServer)
	{
		_servers = [.. servers];
		_sessionId = _sessionListFeature.CurrentSession?.Id;
		_popup?.Open();
		SelectServer(selectedServer);
	}

	void SelectServer(McpServer server)
	{
		_selectedServer = server;
		StateHasChanged();
	}

	async Task ToggleServer(McpServer server)
	{
		if(_isBusy || _sessionId is null) return;
		_isBusy = true;
		StateHasChanged();
		try
		{
			bool isEnabled = server.Status != McpServerStatus.Disabled;
			if(isEnabled)
				await _mcpFeature.DisableServerAsync(_sessionId, server.Name);
			else
				await _mcpFeature.EnableServerAsync(_sessionId, server.Name);

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
		if(_isBusy || _sessionId is null) return;
		_isBusy = true;
		StateHasChanged();
		try
		{
			await _mcpFeature.ReloadAsync(_sessionId);
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
		if(session is null) return;
		_servers = [.. session.Context.McpServers];
		McpServer? refreshed = _servers.FirstOrDefault(s => s.Name == _selectedServer?.Name);
		_selectedServer = refreshed ?? _selectedServer;
	}

	static string GetStatusColor(McpServerStatus status) => status switch
	{
		McpServerStatus.Connected => "text-green-400",
		McpServerStatus.Failed => "text-red-400",
		McpServerStatus.NeedsAuth => "text-yellow-400",
		McpServerStatus.Disabled => "secondary-text",
		_ => "text-yellow-400"
	};
}
