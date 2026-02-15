using Cockpit.Features.Permissions.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace Cockpit.Components;

public partial class PermissionRequestPanel
{
	[Parameter, EditorRequired]
	public required PermissionRequestModel Request { get; set; }

	[Parameter]
	public EventCallback<PermissionDecisionEnum> OnPermissionDecision { get; set; }

	[Inject]
	ILogger<PermissionRequestPanel> Logger { get; set; } = default!;

	bool _showDetailsPopup = false;

	async Task OnDecision(PermissionDecisionEnum decision)
	{
		Logger.LogInformation("OnDecision called: isApproved={IsApproved}, decision={Scope}, sessionId={SessionId}",
			!decision.Equals(PermissionDecisionEnum.Denied), decision, Request.SessionId);

		Logger.LogInformation("Invoking OnPermissionDecision callback");
		await OnPermissionDecision.InvokeAsync(decision);
		Logger.LogInformation("OnPermissionDecision callback completed");
	}
}