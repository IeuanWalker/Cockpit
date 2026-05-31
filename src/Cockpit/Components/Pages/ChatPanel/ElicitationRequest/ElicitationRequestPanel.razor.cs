using Cockpit.Features.ElicitationRequests;
using Cockpit.Features.Sessions;
using GitHub.Copilot;
using GitHub.Copilot.Rpc;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace Cockpit.Components.Pages.ChatPanel.ElicitationRequest;

public sealed partial class ElicitationRequestPanel : ComponentBase, IDisposable
{
	readonly SessionListFeature _sessionListFeature;
	readonly ElicitationFeature _elicitationFeature;
	readonly ILogger<ElicitationRequestPanel> _logger;

	public ElicitationRequestPanel(
		SessionListFeature sessionListFeature,
		ElicitationFeature elicitationFeature,
		ILogger<ElicitationRequestPanel> logger)
	{
		_sessionListFeature = sessionListFeature;
		_elicitationFeature = elicitationFeature;
		_logger = logger;
	}

	protected override void OnInitialized()
	{
		_sessionListFeature.OnStateChanged += OnStateChanged;
	}

	public ElicitationRequestModel? Request => _sessionListFeature.CurrentSession?.PendingElicitationRequests.Values.OrderBy(r => r.Requested).FirstOrDefault();

	bool IsUrlOnly => Request?.Mode?.Value == ElicitationRequestedMode.Url.Value;
	bool IsInline => IsUrlOnly || Request?.Fields.Length <= 4;

	ElicitationFormPopup _formPopup = default!;

	void OpenForm() => _formPopup.Open(Request!);

	void OnDecline()
	{
		ElicitationRequestModel? request = Request;
		if(request is null)
		{
			_logger.LogWarning("OnDecline called but no pending elicitation request found");
			return;
		}

		_logger.LogInformation("Elicitation declined: {RequestId}", request.Id);
		_elicitationFeature.ResolveElicitationRequest(request.Id, new ElicitationResult
		{
			Action = UIElicitationResponseAction.Decline,
			Content = new Dictionary<string, object>()
		});
	}

	void OnCancel()
	{
		ElicitationRequestModel? request = Request;
		if(request is null)
		{
			_logger.LogWarning("OnCancel called but no pending elicitation request found");
			return;
		}

		_logger.LogInformation("Elicitation cancelled: {RequestId}", request.Id);
		_elicitationFeature.ResolveElicitationRequest(request.Id, null);
	}

	void OnStateChanged()
	{
		InvokeAsync(StateHasChanged);
	}

	public void Dispose()
	{
		_sessionListFeature.OnStateChanged -= OnStateChanged;
	}
}
