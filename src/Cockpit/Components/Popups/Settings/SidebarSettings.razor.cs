using Cockpit.Features.AppSettings;
using Cockpit.Features.UIState;
using Cockpit.Features.Workspace;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Popups.Settings;

public partial class SidebarSettings : ComponentBase
{
	readonly IAppSettingsFeature _appSettingsFeature;
	readonly IUIStateFeature _uiStateFeature;
	[Inject] WorkspaceFeature _workspace { get; set; } = default!;
	bool _openSidebarOnSessionOpen;
	string? _status;

	public SidebarSettings(IAppSettingsFeature appSettingsFeature, IUIStateFeature uiStateFeature)
	{
		_appSettingsFeature = appSettingsFeature;
		_uiStateFeature = uiStateFeature;
	}

	protected override void OnInitialized()
	{
		_openSidebarOnSessionOpen = _appSettingsFeature.OpenSidebarOnSessionOpen;
	}

	void OnOpenSidebarChanged()
	{
		_appSettingsFeature.OpenSidebarOnSessionOpen = _openSidebarOnSessionOpen;
	}

	void EditLayout()
	{
		_uiStateFeature.SetRightSidebarOpen(true);
		_status = "Close Settings, arrange the current sidebar, then return here and choose Use current workspace.";
	}

	async Task UseCurrentAsync()
	{
		await _workspace.SaveCurrentAsDefaultAsync();
		_status = "Current tabs, layout, and restorable tool content saved as the default.";
	}

	async Task ResetAsync()
	{
		await _workspace.ResetDefaultAsync();
		_status = "Default restored to Session context and Git changes.";
	}
}
