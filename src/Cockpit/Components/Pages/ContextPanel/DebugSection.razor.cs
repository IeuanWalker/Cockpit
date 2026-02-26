using Cockpit.Features.Permissions;
using Cockpit.Features.Sessions;
using Cockpit.Features.UserInputRequests;

namespace Cockpit.Components.Pages.ContextPanel;

public partial class DebugSection
{
	readonly PermissionFeature _permissionFeature;
	readonly UserInputFeature _userInputFeature;
	readonly SessionListFeature _sessionListFeature;
	public DebugSection(
		PermissionFeature permissionFeature,
		UserInputFeature userInputFeature,
		SessionListFeature sessionListFeature)
	{
		_permissionFeature = permissionFeature;
		_userInputFeature = userInputFeature;
		_sessionListFeature = sessionListFeature;
	}

	bool IsDebugMode =>
#if DEBUG
		true;
#else
        false;
#endif

	void SimulatePermission() => _permissionFeature.SimulateRequestAsync(_sessionListFeature.CurrentSession?.Id!);
	void SimulateUserInputText() => _userInputFeature.SimulateTextRequestAsync(_sessionListFeature.CurrentSession?.Id!);
	void SimulateUserInputChoices() => _userInputFeature.SimulateChoicesRequestAsync(_sessionListFeature.CurrentSession?.Id!);
}