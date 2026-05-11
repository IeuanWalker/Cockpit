using Cockpit.Features.Permissions;
using Cockpit.Features.Sessions;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Pages.ContextPanel;

public sealed partial class SessionCommands : ComponentBase, IDisposable
{
	readonly SessionPermissionFeature _sessionPermissionFeature;
	readonly SessionListFeature _sessionListFeature;
	public SessionCommands(SessionPermissionFeature sessionPermissionFeature, SessionListFeature sessionListFeature)
	{
		_sessionPermissionFeature = sessionPermissionFeature;
		_sessionListFeature = sessionListFeature;
	}

	List<string> Commands { get; set; } = [];

	protected override void OnInitialized()
	{
		RefreshCommands();
		_sessionListFeature.OnStateChanged += OnStateChanged;
	}

	void OnStateChanged()
	{
		InvokeAsync(() =>
		{
			RefreshCommands();
			StateHasChanged();
		});
	}

	void RefreshCommands()
	{
		string? sessionId = _sessionListFeature.CurrentSession?.Id;
		Commands = string.IsNullOrEmpty(sessionId)
			? []
			: _sessionPermissionFeature.GetAll(sessionId);
	}

	void RemoveCommand(string command)
	{
		string? sessionId = _sessionListFeature.CurrentSession?.Id;
		if(string.IsNullOrEmpty(sessionId))
		{
			return;
		}

		_sessionPermissionFeature.Remove(sessionId, command);
	}

	public void Dispose()
	{
		_sessionListFeature.OnStateChanged -= OnStateChanged;
	}
}
