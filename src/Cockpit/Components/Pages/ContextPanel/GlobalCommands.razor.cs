using Cockpit.Features.Permissions;
using Cockpit.Features.UIState;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Pages.ContextPanel;

public sealed partial class GlobalCommands : ComponentBase, IDisposable
{
	[Inject] GlobalPermissionFeature _globalPermissionFeature { get; set; } = null!;
	[Inject] UIStateFeature _uiState { get; set; } = null!;

	public List<string> Commands { get; set; } = [];

	protected override void OnInitialized()
	{
		Commands = _globalPermissionFeature.GetAll();
		_globalPermissionFeature.OnPermissionsChanged += OnPermissionsChanged;
	}

	void OnPermissionsChanged()
	{
		Commands = _globalPermissionFeature.GetAll();
		InvokeAsync(StateHasChanged);
	}

	void OpenCommandsSettings()
	{
		_uiState.OpenSettingsToSection(SettingsSectionEnum.Commands);
	}

	public void Dispose()
	{
		_globalPermissionFeature.OnPermissionsChanged -= OnPermissionsChanged;
	}
}