using Cockpit.Components.Popups;
using Cockpit.Features.Sessions;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components;

public partial class NoSelectedSession : ComponentBase
{
	readonly SessionListFeature _sessionListFeature;
	public NoSelectedSession(SessionListFeature sessionListFeature)
	{
		_sessionListFeature = sessionListFeature;
	}

	CreateSessionPopup? _createSessionPopup;

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
}