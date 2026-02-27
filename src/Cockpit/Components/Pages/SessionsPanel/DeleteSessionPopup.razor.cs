using Cockpit.Components.Controls;
using Cockpit.Features.Sessions;
using Cockpit.Features.Sessions.Models;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Pages.SessionsPanel;

public partial class DeleteSessionPopup : ComponentBase
{
	readonly SessionFeature _sessionFeature;

	public DeleteSessionPopup(SessionFeature sessionFeature)
	{
		_sessionFeature = sessionFeature;
	}

	public SessionModel? Session { get; set; }
	PopupBase _popup = default!;

	public void Open(string sessionId)
	{
		Session = _sessionFeature.Sessions.FirstOrDefault(s => s.Id == sessionId);
		if(Session is not null)
		{
			_popup.Open();
			StateHasChanged();
		}
	}

	async Task Confirm()
	{
		if(Session is not null)
		{
			await _sessionFeature.DeleteSession(Session.Id);
		}

		_popup.Close();
	}

	void Cancel() => _popup.Close();
}
