using Cockpit.Features.Permissions;

namespace Cockpit.Components.Popups.Settings;

public partial class PermissionSettings
{
	readonly GlobalPermissionFeature _globalPermissionFeature;
	List<string> _commands = [];
	bool _showAddDialog;
	string _newCommand = string.Empty;

	public PermissionSettings(GlobalPermissionFeature globalPermissionFeature)
	{
		_globalPermissionFeature = globalPermissionFeature;
	}

	protected override void OnInitialized()
	{
		LoadPermissions();
		_globalPermissionFeature.OnPermissionsChanged += LoadPermissions;
	}

	void LoadPermissions()
	{
		_commands = [.. _globalPermissionFeature.GetAll()];
		StateHasChanged();
	}

	void ShowAddDialog()
	{
		_showAddDialog = true;
		_newCommand = string.Empty;
	}

	void HideAddDialog()
	{
		_showAddDialog = false;
	}

	void AddPermission()
	{
		if(string.IsNullOrWhiteSpace(_newCommand))
		{
			return;
		}

		_globalPermissionFeature.Add(_newCommand);

		HideAddDialog();
	}

	void RemovePermission(string command)
	{
		_globalPermissionFeature.Remove(command);
	}

	public void Dispose()
	{
		_globalPermissionFeature.OnPermissionsChanged -= LoadPermissions;
	}
}