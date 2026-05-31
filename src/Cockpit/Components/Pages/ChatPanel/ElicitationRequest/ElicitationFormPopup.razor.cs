using Cockpit.Components.Controls;
using Cockpit.Features.ElicitationRequests;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Pages.ChatPanel.ElicitationRequest;

public sealed partial class ElicitationFormPopup : ComponentBase
{
	readonly ElicitationFeature _elicitationFeature;

	public ElicitationFormPopup(ElicitationFeature elicitationFeature)
	{
		_elicitationFeature = elicitationFeature;
	}

	PopupBase _popup = default!;

	/// <summary>
	/// The captured request at the time the popup was opened.
	/// Prevents submitting to the wrong request if a new one arrives while open.
	/// </summary>
	ElicitationRequestModel? _request;

	public void Open(ElicitationRequestModel request)
	{
		_request = request;
		_popup.Open();
	}

	string GetTitle() => _request is not null
		? $"Form from {_request.ElicitationSource}"
		: "Form Request";
}
