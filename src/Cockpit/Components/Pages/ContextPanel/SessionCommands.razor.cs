using Cockpit.Features.Permissions;
using Cockpit.Services;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Pages.ContextPanel;

public sealed partial class SessionCommands : ComponentBase, IDisposable
{
	[Inject] SessionPermissionFeature _sessionPermissionFeature { get; set; } = null!;
	[Inject] UnifiedSessionManager _sessionManager { get; set; } = null!;

	public List<string> Commands { get; set; } = [];

	protected override void OnInitialized()
	{
		RefreshCommands();
		_sessionManager.OnStateChanged += OnStateChanged;
	}

	void OnStateChanged()
	{
		RefreshCommands();
		InvokeAsync(StateHasChanged);
	}

	void RefreshCommands()
	{
		string? sessionId = _sessionManager.CurrentSession?.Id;
		Commands = string.IsNullOrEmpty(sessionId)
			? []
			: _sessionPermissionFeature.GetAll(sessionId);
	}

	void RemoveCommand(string command)
	{
		string? sessionId = _sessionManager.CurrentSession?.Id;
		if(string.IsNullOrEmpty(sessionId))
		{
			return;
		}

		_sessionPermissionFeature.Remove(sessionId, command);
	}

	public void Dispose()
	{
		_sessionManager.OnStateChanged -= OnStateChanged;
	}
}
