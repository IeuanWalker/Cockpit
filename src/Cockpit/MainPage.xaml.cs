using Cockpit.Components.Popups.Settings;
using Cockpit.Features.Auth;
using Cockpit.Features.Sdk;
using Cockpit.Features.Sessions;
using Cockpit.Features.Splash;
using Cockpit.Features.Theme;
using GitHub.Copilot;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Cockpit;

public partial class MainPage : ContentPage
{
	readonly SplashFeature _splashFeature;
	readonly SessionFeature _sessionFeature;
	readonly ThemeStateFeature _themeStateFeature;
	readonly CopilotClientFeature _copilotClientFeature;
	readonly AuthFeature _authFeature;
	readonly ILogger<MainPage> _logger;

	int _splashHidden;
	int _blazorReady;
	int _initComplete;
	int _authInProgress;
	int _initStarted;
	bool _isEnterprise;
	string _authUrl = string.Empty;
	string _deviceCode = string.Empty;

	public MainPage(
		SplashFeature splashFeature,
		SessionFeature sessionFeature,
		ThemeStateFeature themeStateFeature,
		CopilotClientFeature copilotClientFeature,
		AuthFeature authFeature,
		ILogger<MainPage> logger)
	{
		InitializeComponent();

		_splashFeature = splashFeature;
		_sessionFeature = sessionFeature;
		_themeStateFeature = themeStateFeature;
		_copilotClientFeature = copilotClientFeature;
		_authFeature = authFeature;
		_logger = logger;

#if WINDOWS
		ConfigureWindowsContextMenu();
#endif

		_splashFeature.OnBlazorReady += OnBlazorReady;

	}

	void OnBlazorReady()
	{
		Dispatcher.Dispatch(() =>
		{
			Interlocked.Exchange(ref _blazorReady, 1);
			_ = TryHideSplash();

#if DEBUG
			DiagnosticsSettings.OpenLogViewer(_themeStateFeature.IsLightTheme);
#endif
		});
	}

	async Task TryHideSplash()
	{
		if(Volatile.Read(ref _blazorReady) == 0 || Volatile.Read(ref _initComplete) == 0)
		{
			return;
		}

		await HideSplash();
	}

	async Task HideSplash()
	{
		if(Interlocked.Exchange(ref _splashHidden, 1) != 0)
		{
			return;
		}

		await splashOverlay.FadeToAsync(0, 400, Easing.CubicOut);
		splashOverlay.IsVisible = false;
		// Remove from tree so it cannot intercept input (WinUI hidden views can block scroll)
		((Grid)Content).Children.Remove(splashOverlay);
		blazorWebView.Focus();
	}

	protected override void OnAppearing()
	{
		base.OnAppearing();
#if WINDOWS
		ConfigureWindowsContextMenuOnAppearing();
#endif

		_ = InitializeAsync();
	}

	async Task InitializeAsync()
	{
		if(Interlocked.Exchange(ref _initStarted, 1) != 0)
		{
			return;
		}

		try
		{
			UpdateStatus("Starting Copilot...");

			CopilotClient client = await _copilotClientFeature.GetClientAsync();

			UpdateStatus("Checking authentication...");
			GetAuthStatusResponse authStatus = await client.GetAuthStatusAsync();

			_logger.LogInformation(
				"Auth status: IsAuthenticated={IsAuth}, Host={Host}, Login={Login}, Status={Status}",
				authStatus.IsAuthenticated, authStatus.Host, authStatus.Login, authStatus.StatusMessage);

			if(!authStatus.IsAuthenticated)
			{
				_logger.LogInformation("Not authenticated. Showing sign-in prompt");
				ShowAuthRequired();
				return;
			}

			await CompleteInitialization(authStatus);
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Initialization failed");
			UpdateStatus($"Error: {ex.Message}");
		}
	}

	async void OnSignInClicked(object? sender, EventArgs e)
	{
		Interlocked.Exchange(ref _authInProgress, 1);

		try
		{
			if(_isEnterprise && string.IsNullOrWhiteSpace(enterpriseHostEntry.Text))
			{
				UpdateStatus("Please enter your enterprise host URL.");
				return;
			}

			string? host = GetSelectedHost();
			ShowAuthenticating();

			bool loginSuccess = await _authFeature.RunLoginAsync(
				host: host,
				onDeviceFlow: info => Dispatcher.Dispatch(() => ShowDeviceCode(info)),
				cancellationToken: CancellationToken.None);

			if(!loginSuccess)
			{
				_logger.LogWarning("copilot login process exited with failure");
				ShowAuthFailed("Login failed. Please try again.");
				return;
			}

			// Restart client so it picks up the new credentials from the bundled CLI's keychain
			Dispatcher.Dispatch(() =>
			{
				deviceCodeSection.IsVisible = false;
				loadingSection.IsVisible = true;
			});
			UpdateStatus("Signing in...");

			await _copilotClientFeature.RestartAsync();
			CopilotClient client = await _copilotClientFeature.GetClientAsync();
			GetAuthStatusResponse authStatus = await client.GetAuthStatusAsync();

			_logger.LogInformation(
				"Post-login auth status: IsAuthenticated={IsAuth}, Host={Host}, Login={Login}, Status={Status}",
				authStatus.IsAuthenticated, authStatus.Host, authStatus.Login, authStatus.StatusMessage);

			// Trust the CLI exit code for enterprise accounts — the SDK may not
			// recognise enterprise auth state via GetAuthStatusAsync
			await CompleteInitialization(authStatus);
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Sign-in flow failed");
			ShowAuthFailed($"Sign-in failed: {ex.Message}");
		}
		finally
		{
			Interlocked.Exchange(ref _authInProgress, 0);
		}
	}

	async Task CompleteInitialization(GetAuthStatusResponse authStatus)
	{
		_logger.LogInformation("Authenticated as {Login} on {Host}", authStatus.Login, authStatus.Host);
		Dispatcher.Dispatch(() =>
		{
			authRequiredSection.IsVisible = false;
			deviceCodeSection.IsVisible = false;
			loadingSection.IsVisible = true;
			splashSpinner.IsRunning = true;
		});
		UpdateStatus($"Signed in as {authStatus.Login}");

		await Task.Delay(600); // Brief pause to show signed-in status

		UpdateStatus("Loading sessions...");
		await _sessionFeature.LoadExistingSessions();

		Interlocked.Exchange(ref _initComplete, 1);
		Dispatcher.Dispatch(() => _ = TryHideSplash());
	}

	void ShowAuthRequired()
	{
		Dispatcher.Dispatch(() =>
		{
			loadingSection.IsVisible = false;
			deviceCodeSection.IsVisible = false;
			authRequiredSection.IsVisible = true;

			// Pre-fill enterprise host from config if available
			string? savedHost = AuthFeature.ReadHostFromConfig();
			if(savedHost is not null && !savedHost.Equals("https://github.com", StringComparison.OrdinalIgnoreCase))
			{
				_isEnterprise = true;
				enterpriseHostEntry.Text = savedHost;
				UpdateAccountTypeSelection();
			}
		});
	}

	void ShowAuthenticating()
	{
		Dispatcher.Dispatch(() =>
		{
			authRequiredSection.IsVisible = false;
			deviceCodeSection.IsVisible = false;
			loadingSection.IsVisible = true;
			splashSpinner.IsRunning = true;
		});
		UpdateStatus("Starting sign-in flow...");
	}

	void ShowDeviceCode(AuthFeature.DeviceFlowInfo info)
	{
		_authUrl = info.Url;
		_deviceCode = info.Code;

		loadingSection.IsVisible = false;
		authRequiredSection.IsVisible = false;
		deviceCodeSection.IsVisible = true;
		deviceCodeLabel.Text = info.Code;
		authUrlLabel.Text = info.Url;
	}

	void ShowAuthFailed(string message)
	{
		Dispatcher.Dispatch(() =>
		{
			deviceCodeSection.IsVisible = false;
			loadingSection.IsVisible = false;
			authRequiredSection.IsVisible = true;
		});
		UpdateStatus(message);
	}

	void OnGitHubComSelected(object? sender, TappedEventArgs e)
	{
		_isEnterprise = false;
		UpdateAccountTypeSelection();
	}

	void OnEnterpriseSelected(object? sender, TappedEventArgs e)
	{
		_isEnterprise = true;
		UpdateAccountTypeSelection();
	}

	void UpdateAccountTypeSelection()
	{
		// Selected style: blue tint
		githubComOption.BackgroundColor = _isEnterprise ? Color.FromArgb("#1F2937") : Color.FromArgb("#1E3A5F");
		githubComOption.Stroke = new SolidColorBrush(_isEnterprise ? Color.FromArgb("#374151") : Color.FromArgb("#3B82F6"));

		enterpriseOption.BackgroundColor = _isEnterprise ? Color.FromArgb("#1E3A5F") : Color.FromArgb("#1F2937");
		enterpriseOption.Stroke = new SolidColorBrush(_isEnterprise ? Color.FromArgb("#3B82F6") : Color.FromArgb("#374151"));

		// Update label colors
		((Label?)githubComOption.Content)?.TextColor = _isEnterprise ? Color.FromArgb("#9CA3AF") : Colors.White;
		((Label?)enterpriseOption.Content)?.TextColor = _isEnterprise ? Colors.White : Color.FromArgb("#9CA3AF");

		enterpriseHostSection.IsVisible = _isEnterprise;
	}

	string? GetSelectedHost()
	{
		if(!_isEnterprise)
		{
			return null;
		}

		string host = enterpriseHostEntry.Text?.Trim() ?? string.Empty;
		if(string.IsNullOrWhiteSpace(host))
		{
			return null;
		}

		// Ensure the URL has a scheme
		if(!host.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
		{
			host = $"https://{host}";
		}

		return host;
	}

	void UpdateStatus(string text)
	{
		Dispatcher.Dispatch(() => statusLabel.Text = text);
	}

	async void OnCopyCodeClicked(object? sender, EventArgs e)
	{
		if(!string.IsNullOrEmpty(_deviceCode))
		{
			await Clipboard.SetTextAsync(_deviceCode);
			copyCodeButton.Text = "Copied!";
			await Task.Delay(2000);
			copyCodeButton.Text = "Copy";
		}
	}

	async void OnAuthUrlTapped(object? sender, TappedEventArgs e)
	{
		if(!string.IsNullOrEmpty(_authUrl))
		{
			await Launcher.OpenAsync(new Uri(_authUrl));
		}
	}

	protected override void OnDisappearing()
	{
		base.OnDisappearing();
		_splashFeature.OnBlazorReady -= OnBlazorReady;
#if WINDOWS
		TeardownWindowsContextMenu();
#endif
	}

	public async Task InvokeJavaScriptAsync(string script)
	{
		try
		{
			await blazorWebView.TryDispatchAsync(async (sp) =>
			{
				IJSRuntime jsRuntime = sp.GetRequiredService<IJSRuntime>();
				try
				{
					await jsRuntime.InvokeVoidAsync("eval", script);
				}
				catch
				{
					// Handle error silently
				}
			});
		}
		catch
		{
			// Handle error silently
		}
	}
}
