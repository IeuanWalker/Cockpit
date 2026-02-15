using Cockpit.Features.Permissions.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace Cockpit.Components;

public partial class PermissionRequestPanel
{
	[Parameter, EditorRequired]
	public required PermissionRequestModel Request { get; set; }

	[Parameter]
	public EventCallback<PermissionDecisionModel> OnPermissionDecision { get; set; }

	[Inject]
	ILogger<PermissionRequestPanel> Logger { get; set; } = default!;

	bool _showDetailsPopup = false;

	async Task OnDecision(bool isApproved, PermissionScope scope)
	{
		Logger.LogInformation("OnDecision called: isApproved={IsApproved}, scope={Scope}, sessionId={SessionId}",
			isApproved, scope, Request.SessionId);

		PermissionDecisionModel decision = new()
		{
			IsApproved = isApproved,
			Scope = scope
		};

		Logger.LogInformation("Invoking OnPermissionDecision callback");
		await OnPermissionDecision.InvokeAsync(decision);
		Logger.LogInformation("OnPermissionDecision callback completed");
	}
}