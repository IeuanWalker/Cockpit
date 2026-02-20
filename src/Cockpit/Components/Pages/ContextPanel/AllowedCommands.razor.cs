using Cockpit.Features.Permissions;
using Cockpit.Features.Sessions;
using Cockpit.Features.UIState;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Pages.ContextPanel;

public sealed partial class AllowedCommands : ComponentBase, IDisposable
{
	[Inject] UIStateFeature _uiState { get; set; } = null!;
	[Inject] GlobalPermissionFeature _globalPermissionFeature { get; set; } = null!;
	[Inject] SessionPermissionFeature _sessionPermissionFeature { get; set; } = null!;
	[Inject] SessionListFeature _sessionManager { get; set; } = null!;

	public int TotalCommandsCount { get; set; }

	protected override void OnInitialized()
	{
		RefreshCount();
		_globalPermissionFeature.OnPermissionsChanged += OnStateChanged;
		_sessionManager.OnStateChanged += OnStateChanged;
	}

	void OnStateChanged()
	{
		RefreshCount();
		InvokeAsync(StateHasChanged);
	}

	void RefreshCount()
	{
		int globalCount = _globalPermissionFeature.GetAll().Count;
		string? sessionId = _sessionManager.CurrentSession?.Id;
		int sessionCount = string.IsNullOrEmpty(sessionId) ? 0 : _sessionPermissionFeature.GetAll(sessionId).Count;
		TotalCommandsCount = globalCount + sessionCount;
	}

	public void Dispose()
	{
		_globalPermissionFeature.OnPermissionsChanged -= OnStateChanged;
		_sessionManager.OnStateChanged -= OnStateChanged;
	}
}
