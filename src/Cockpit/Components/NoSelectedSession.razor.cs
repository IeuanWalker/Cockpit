using Cockpit.Components.Popups;
using Cockpit.Features.Sessions;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components;

public partial class NoSelectedSession : ComponentBase
{
	[Inject] SessionListFeature _sessionManager { get; set; } = default!;
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