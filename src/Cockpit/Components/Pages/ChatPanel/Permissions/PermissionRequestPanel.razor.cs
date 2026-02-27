using Cockpit.Features.Permissions;
using Cockpit.Features.Permissions.Models;
using Cockpit.Features.Sessions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace Cockpit.Components.Pages.ChatPanel.Permissions;

public partial class PermissionRequestPanel : ComponentBase, IDisposable
{
	readonly SessionListFeature _sessionManager;
	readonly PermissionFeature _permissionFeature;
	readonly ILogger<PermissionRequestPanel> _logger;

	public PermissionRequestPanel(
		SessionListFeature sessionManager,
		PermissionFeature permissionFeature,
		ILogger<PermissionRequestPanel> logger)
	{
		_sessionManager = sessionManager;
		_permissionFeature = permissionFeature;
		_logger = logger;
	}

	protected override void OnInitialized()
	{
		_sessionManager.OnStateChanged += OnStateChanged;
	}

	bool _showDropdown = false;
	PermissionDecisionEnum _selectedAllowOption = PermissionDecisionEnum.Once;
	PermissionDetailsPopup _moreInfoPopup = default!;
	PermissionRequestModel? Request => _sessionManager.CurrentSession?.PendingPermissionRequests?.Values.OrderBy(r => r.Requested).FirstOrDefault();

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

	void OpenMoeInfoPopup() => _moreInfoPopup.Open();

	void OnStateChanged()
	{
		InvokeAsync(StateHasChanged);
	}

	Task OnDecision(PermissionDecisionEnum decision)
	{
		PermissionRequestModel? currentRequest = Request;
		if(currentRequest is null)
		{
			_logger.LogWarning("OnDecision called but no pending permission request was found");
			return Task.CompletedTask;
		}

		_logger.LogInformation("OnDecision called: isApproved={IsApproved}, decision={Scope}, sessionId={SessionId}",
			!decision.Equals(PermissionDecisionEnum.Denied), decision, currentRequest.SessionId);

		// Reset to default so the next request always starts with "Allow Once"
		_selectedAllowOption = PermissionDecisionEnum.Once;
		_showDropdown = false;

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