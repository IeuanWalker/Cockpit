using Cockpit.Features.Permissions;

namespace Cockpit.Components.Popups.Settings;

public partial class PermissionSettings
{
	readonly GlobalPermissionFeature _globalPermissionFeature;
	readonly GlobalDenyFeature _globalDenyFeature;

	List<string> _allowedCommands = [];
	List<string> _deniedCommands = [];

	bool _showAddAllowDialog;
	bool _showAddDenyDialog;
	string _newAllowCommand = string.Empty;
	string _newDenyCommand = string.Empty;

	public PermissionSettings(GlobalPermissionFeature globalPermissionFeature, GlobalDenyFeature globalDenyFeature)
	{
		_globalPermissionFeature = globalPermissionFeature;
		_globalDenyFeature = globalDenyFeature;
	}

	protected override void OnInitialized()
	{
		LoadPermissions();
		_globalPermissionFeature.OnPermissionsChanged += LoadPermissions;
		_globalDenyFeature.OnDenyListChanged += LoadPermissions;
	}

	void LoadPermissions()
	{
		_allowedCommands = [.. _globalPermissionFeature.GetAll()];
		_deniedCommands = [.. _globalDenyFeature.GetAll()];
		StateHasChanged();
	}

	// ---- Allow list ----

	void ShowAddAllowDialog()
	{
		_showAddAllowDialog = true;
		_newAllowCommand = string.Empty;
	}

	void HideAddAllowDialog() => _showAddAllowDialog = false;

	void AddAllowedPermission()
	{
		if(string.IsNullOrWhiteSpace(_newAllowCommand))
		{
			return;
		}

		_globalPermissionFeature.Add(_newAllowCommand);
		HideAddAllowDialog();
	}

	void RemoveAllowedPermission(string command) => _globalPermissionFeature.Remove(command);

	// ---- Deny list ----

	void ShowAddDenyDialog()
	{
		_showAddDenyDialog = true;
		_newDenyCommand = string.Empty;
	}

	void HideAddDenyDialog() => _showAddDenyDialog = false;

	void AddDeniedCommand()
	{
		if(string.IsNullOrWhiteSpace(_newDenyCommand))
		{
			return;
		}

		_globalDenyFeature.Add(_newDenyCommand);
		HideAddDenyDialog();
	}

	void RemoveDeniedCommand(string command) => _globalDenyFeature.Remove(command);

	public void Dispose()
	{
		_globalPermissionFeature.OnPermissionsChanged -= LoadPermissions;
		_globalDenyFeature.OnDenyListChanged -= LoadPermissions;
	}
}