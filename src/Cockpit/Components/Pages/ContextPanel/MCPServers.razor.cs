using Cockpit.Services;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Pages.ContextPanel;

public partial class MCPServers : ComponentBase, IDisposable
{
	[Inject] UIStateService _uiState { get; set; } = null!;
	[Inject] UnifiedSessionManager _sessionManager { get; set; } = default!;

	string McpServerUrl => _sessionManager.CurrentSession?.Context?.McpServerUrl ?? string.Empty;
	bool McpServerConnected => _sessionManager.CurrentSession?.Context?.McpServerConnected ?? false;

	protected override void OnInitialized()
	{
		_sessionManager.OnStateChanged += OnStateChanged;
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
			_sessionManager.OnStateChanged -= OnStateChanged;
		}
	}
}