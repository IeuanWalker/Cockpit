using Cockpit.Features.Permissions;
using Cockpit.Features.Sessions;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Pages.ContextPanel;

public sealed partial class AllowedCommands : ComponentBase, IDisposable
{
	readonly GlobalPermissionFeature _globalPermissionFeature;
	readonly SessionPermissionFeature _sessionPermissionFeature;
	readonly SessionListFeature _sessionListFeature;

	public AllowedCommands(
		GlobalPermissionFeature globalPermissionFeature,
		SessionPermissionFeature sessionPermissionFeature,
		SessionListFeature sessionListFeature)
	{
		_globalPermissionFeature = globalPermissionFeature;
		_sessionPermissionFeature = sessionPermissionFeature;
		_sessionListFeature = sessionListFeature;
	}

	int TotalCommandsCount { get; set; }

	protected override void OnInitialized()
	{
		RefreshCount();
		_globalPermissionFeature.OnPermissionsChanged += OnStateChanged;
		_sessionListFeature.OnStateChanged += OnStateChanged;
	}

	void OnStateChanged()
	{
		InvokeAsync(() =>
		{
			RefreshCount();
			StateHasChanged();
		});
	}

	int _renderedCommandsCount = -1;

	protected override bool ShouldRender()
	{
		if(_renderedCommandsCount == TotalCommandsCount)
		{
			return false;
		}

		_renderedCommandsCount = TotalCommandsCount;
		return true;
	}

	void RefreshCount()
	{
		int globalCount = _globalPermissionFeature.GetAll().Count;
		string? sessionId = _sessionListFeature.CurrentSession?.Id;
		int sessionCount = string.IsNullOrEmpty(sessionId) ? 0 : _sessionPermissionFeature.GetAll(sessionId).Count;
		TotalCommandsCount = globalCount + sessionCount;
	}

	public void Dispose()
	{
		_globalPermissionFeature.OnPermissionsChanged -= OnStateChanged;
		_sessionListFeature.OnStateChanged -= OnStateChanged;
	}
}
