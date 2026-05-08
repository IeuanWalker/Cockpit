using Cockpit.Features.Sessions;
using GitHub.Copilot.SDK.Rpc;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Pages.ContextPanel;

public sealed partial class MCPServers : ComponentBase, IDisposable
{
McpServerInfoPopup? _mcpServerInfoPopup;
readonly SessionListFeature _sessionListFeature;

public MCPServers(SessionListFeature sessionListFeature)
{
_sessionListFeature = sessionListFeature;
}

List<McpServer> _allServers = [];
McpServer? _selectedServer;

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

void ShowMcpServerInfo(McpServer server) => _mcpServerInfoPopup?.Open(_allServers, server);

List<McpServer> _renderedServers = [];
McpServer? _renderedSelected;

protected override bool ShouldRender()
{
if(ReferenceEquals(_allServers, _renderedServers) && ReferenceEquals(_renderedSelected, _selectedServer))
return false;
_renderedServers = _allServers;
_renderedSelected = _selectedServer;
return true;
}

void Refresh()
{
_allServers = [.. _sessionListFeature.CurrentSession?.Context.McpServers ?? []];
_selectedServer = null;
}

public void Dispose()
{
_sessionListFeature.OnStateChanged -= OnStateChanged;
}
}