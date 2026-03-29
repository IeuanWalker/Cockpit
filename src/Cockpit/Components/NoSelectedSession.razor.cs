using Cockpit.Components.Popups;
using Cockpit.Features.Sessions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace Cockpit.Components;

public partial class NoSelectedSession : ComponentBase
{
	readonly SessionListFeature _sessionListFeature;
	readonly ILogger<NoSelectedSession> _logger;

	public NoSelectedSession(SessionListFeature sessionListFeature, ILogger<NoSelectedSession> logger)
	{
		_sessionListFeature = sessionListFeature;
		_logger = logger;
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
			_logger.LogError(ex, "Failed to open create session popup");
		}
	}
}
