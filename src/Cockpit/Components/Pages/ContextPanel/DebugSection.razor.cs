using Cockpit.Features.ElicitationRequests;
using Cockpit.Features.Permissions;
using Cockpit.Features.Sessions;
using Cockpit.Features.UserInputRequests;

namespace Cockpit.Components.Pages.ContextPanel;

public partial class DebugSection
{
	readonly PermissionFeature _permissionFeature;
	readonly UserInputFeature _userInputFeature;
	readonly ElicitationFeature _elicitationFeature;
	readonly SessionListFeature _sessionListFeature;
	readonly SessionFeature _sessionFeature;
	public DebugSection(
		PermissionFeature permissionFeature,
		UserInputFeature userInputFeature,
		ElicitationFeature elicitationFeature,
		SessionListFeature sessionListFeature,
		SessionFeature sessionFeature)
	{
		_permissionFeature = permissionFeature;
		_userInputFeature = userInputFeature;
		_elicitationFeature = elicitationFeature;
		_sessionListFeature = sessionListFeature;
		_sessionFeature = sessionFeature;
	}

	bool IsDebugMode =>
#if DEBUG
		true;
#else
        false;
#endif

	void SimulatePermission1() => _permissionFeature.SimulateRequest1(_sessionListFeature.CurrentSession?.Id!);
	void SimulatePermission2() => _permissionFeature.SimulateRequest2(_sessionListFeature.CurrentSession?.Id!);
	void SimulateUserInputText() => _userInputFeature.SimulateTextRequest(_sessionListFeature.CurrentSession?.Id!);
	void SimulateUserInputChoices() => _userInputFeature.SimulateChoicesRequest(_sessionListFeature.CurrentSession?.Id!);
	void SimulateUserInputTextAndChoices() => _userInputFeature.SimulateTextAndChoicesRequest(_sessionListFeature.CurrentSession?.Id!);
	void SimulateElicitationSimpleForm() => _elicitationFeature.SimulateSimpleFormRequest(_sessionListFeature.CurrentSession?.Id!);
	void SimulateElicitationEnumBool() => _elicitationFeature.SimulateEnumAndBoolRequest(_sessionListFeature.CurrentSession?.Id!);
	void SimulateElicitationFullForm() => _elicitationFeature.SimulateFullFormRequest(_sessionListFeature.CurrentSession?.Id!);
	void SimulateElicitationUrlMode() => _elicitationFeature.SimulateUrlModeRequest(_sessionListFeature.CurrentSession?.Id!);
	Task ReplaySession() => _sessionFeature.ReplayCurrentSessionAsync();
}