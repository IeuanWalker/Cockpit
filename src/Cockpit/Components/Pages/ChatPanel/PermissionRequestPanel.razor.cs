using Cockpit.Features.Permissions;
using Cockpit.Features.Permissions.Models;
using Cockpit.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace Cockpit.Components.Pages.ChatPanel;

public partial class PermissionRequestPanel : ComponentBase, IDisposable
{
    bool _showDropdown = false;
    PermissionDecisionEnum _selectedAllowOption = PermissionDecisionEnum.Once;

    string GetAllowLabel(PermissionDecisionEnum option) => option switch
    {
        PermissionDecisionEnum.Once => "Allow Once",
        PermissionDecisionEnum.Session => "Allow for this Session",
        PermissionDecisionEnum.Global => "Allow globally",
        _ => "Allow"
    };

    void ToggleDropdown()
    {
        _showDropdown = !_showDropdown;
        StateHasChanged();
    }

    void SelectAllowOption(PermissionDecisionEnum option)
    {
        _selectedAllowOption = option;
        _showDropdown = false;
        StateHasChanged();
    }

    [Inject] UnifiedSessionManager _sessionManager { get; set; } = default!;

    [Inject] PermissionFeature _permissionFeature { get; set; } = default!;

    [Inject]
    ILogger<PermissionRequestPanel> Logger { get; set; } = default!;

    bool _showDetailsPopup = false;
    PermissionRequestModel? Request => _sessionManager.CurrentSession?.PendingPermissionRequests?.Values.FirstOrDefault();

	protected override void OnInitialized()
	{
		_sessionManager.OnStateChanged += OnStateChanged;
	}

	void OnStateChanged()
	{
		InvokeAsync(StateHasChanged);
	}

	Task OnDecision(PermissionDecisionEnum decision)
	{
		PermissionRequestModel? currentRequest = Request;
		if(currentRequest is null)
		{
			Logger.LogWarning("OnDecision called but no pending permission request was found");
			return Task.CompletedTask;
		}

		Logger.LogInformation("OnDecision called: isApproved={IsApproved}, decision={Scope}, sessionId={SessionId}",
			!decision.Equals(PermissionDecisionEnum.Denied), decision, currentRequest.SessionId);

		_permissionFeature.ResolvePermissionRequest(currentRequest.Id, decision);
		return Task.CompletedTask;
	}

	public void Dispose()
	{
		Dispose(true);
		GC.SuppressFinalize(this);
	}

	protected virtual void Dispose(bool disposing)
	{
		if(disposing)
		{
			_sessionManager.OnStateChanged -= OnStateChanged;
		}
	}
}
