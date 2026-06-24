using Cockpit.Features.Sdk;
using GitHub.Copilot;
using Microsoft.Extensions.Logging;

namespace Cockpit.Features.Auth;

public enum AuthState
{
	Checking,
	Authenticated,
	NotAuthenticated
}

/// <summary>
/// Checks whether the Copilot SDK is authenticated on startup and exposes the result
/// for the UI to gate access behind an auth guide when needed.
/// </summary>
public sealed class AuthCheckFeature
{
	readonly CopilotClientFeature _clientFeature;
	readonly ILogger<AuthCheckFeature> _logger;

	public event Action? OnStateChanged;

	public AuthState State { get; private set; } = AuthState.Checking;

	public AuthCheckFeature(CopilotClientFeature clientFeature, ILogger<AuthCheckFeature> logger)
	{
		_clientFeature = clientFeature;
		_logger = logger;
	}

	/// <summary>
	/// Checks whether the SDK can authenticate. When <paramref name="isRecheck"/> is
	/// <see langword="true"/> the existing client is stopped first so a fresh process
	/// picks up any credentials the user may have added since the last attempt.
	/// On a re-check the state stays <see cref="AuthState.NotAuthenticated"/> until the
	/// result is known, so the auth guide remains visible during the check.
	/// </summary>
	public async Task CheckAuthAsync(bool isRecheck = false, CancellationToken cancellationToken = default)
	{
		// Only blank the screen on the initial check (splash is still covering the window).
		// Re-checks keep showing the auth guide while the spinner runs on the button.
		if(!isRecheck)
		{
			State = AuthState.Checking;
			OnStateChanged?.Invoke();
		}

		try
		{
			if(isRecheck)
			{
				_logger.LogInformation("Auth re-check: restarting SDK client");
				await _clientFeature.StopAsync();
			}

			CopilotClient client = await _clientFeature.GetClientAsync(cancellationToken);
			GetAuthStatusResponse status = await client.GetAuthStatusAsync(cancellationToken);

			if(status.IsAuthenticated)
			{
				State = AuthState.Authenticated;
				_logger.LogInformation("Auth check passed (login={Login}, host={Host})", status.Login, status.Host);
			}
			else
			{
				State = AuthState.NotAuthenticated;
				_logger.LogWarning("Auth check: not authenticated — {StatusMessage}", status.StatusMessage);
			}
		}
		catch(OperationCanceledException)
		{
			throw;
		}
		catch(Exception ex)
		{
			State = AuthState.NotAuthenticated;
			_logger.LogWarning(ex, "Auth check failed — showing setup guide");
		}
		finally
		{
			OnStateChanged?.Invoke();
		}
	}
}
