using Cockpit.Components.Popups.Settings;
using Cockpit.Features.Permissions;
using Cockpit.Features.UIState;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Pages.ContextPanel;

public sealed partial class GlobalCommands : ComponentBase, IDisposable
{
	readonly GlobalPermissionFeature _globalPermissionFeature;
	public GlobalCommands(GlobalPermissionFeature globalPermissionFeature)
	{
		_globalPermissionFeature = globalPermissionFeature;
	}

	List<string> Commands { get; set; } = [];

	SettingsPopup _settingsPopup = default!;

	protected override void OnInitialized()
	{
		Commands = _globalPermissionFeature.GetAll();
		_globalPermissionFeature.OnPermissionsChanged += OnPermissionsChanged;
	}

	void OnPermissionsChanged()
	{
		InvokeAsync(() =>
		{
			Commands = _globalPermissionFeature.GetAll();
			StateHasChanged();
		});
	}

	void OpenCommandsSettings()
	{
		_settingsPopup.OpenToSection(SettingsSectionEnum.Commands);
	}

	public void Dispose()
	{
		_globalPermissionFeature.OnPermissionsChanged -= OnPermissionsChanged;
	}
}