using Cockpit.Features.Auth;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace Cockpit.Components;

public partial class NotAuthenticatedScreen : ComponentBase
{
	readonly AuthCheckFeature _authCheckFeature;
	readonly ILogger<NotAuthenticatedScreen> _logger;

	public NotAuthenticatedScreen(AuthCheckFeature authCheckFeature, ILogger<NotAuthenticatedScreen> logger)
	{
		_authCheckFeature = authCheckFeature;
		_logger = logger;
	}

	bool _isChecking;

#if WINDOWS
	readonly bool _isWindows = true;
#else
	readonly bool _isWindows = false;
#endif

	async Task CheckAgain()
	{
		if(_isChecking)
		{
			return;
		}

		_isChecking = true;
		StateHasChanged();

		try
		{
			await _authCheckFeature.CheckAuthAsync(isRecheck: true);
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Unexpected error during auth re-check");
		}
		finally
		{
			_isChecking = false;
			StateHasChanged();
		}
	}

	async Task OpenGhDocs()
	{
		try
		{
			await Launcher.OpenAsync(new Uri("https://cli.github.com"));
		}
		catch(Exception ex)
		{
			_logger.LogWarning(ex, "Failed to open gh docs URL");
		}
	}
}
