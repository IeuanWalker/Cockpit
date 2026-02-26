using Cockpit.Components.Popups;
using Cockpit.Features.Permissions;
using Cockpit.Features.Sessions;
using Cockpit.Features.UserInputRequests;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components;

public partial class NoSelectedSession : ComponentBase
{
	[Inject] SessionListFeature _sessionManager { get; set; } = default!;
#if DEBUG
	[Inject] PermissionFeature _permissionFeature { get; set; } = default!;
	[Inject] UserInputFeature _userInputFeature { get; set; } = default!;
#endif
	CreateSessionPopup? _createSessionPopup;

#if DEBUG
	bool IsDebugMode => true;
#else
	bool IsDebugMode => false;
#endif

	async Task CreateNewSession()
	{
		try
		{
			_createSessionPopup?.Open();
		}
		catch(Exception ex)
		{
			Console.Error.WriteLine($"Failed to open directory dialog: {ex.Message}");
		}
	}

#if DEBUG
	void SimulatePermission()
	{
		string? sessionId = _sessionManager.CurrentSession?.Id;
		if(sessionId is null) return;
		_ = _permissionFeature.SimulateRequestAsync(sessionId);
	}

	void SimulateUserInputText()
	{
		string? sessionId = _sessionManager.CurrentSession?.Id;
		if(sessionId is null) return;
		_ = _userInputFeature.SimulateTextRequestAsync(sessionId);
	}

	void SimulateUserInputChoices()
	{
		string? sessionId = _sessionManager.CurrentSession?.Id;
		if(sessionId is null) return;
		_ = _userInputFeature.SimulateChoicesRequestAsync(sessionId);
	}
#else
	void SimulatePermission() { }
	void SimulateUserInputText() { }
	void SimulateUserInputChoices() { }
#endif
}