using Cockpit.Components.Popups.Settings;
using Cockpit.Features.Sessions;
using Cockpit.Features.Splash;
using Cockpit.Features.Theme;
using Microsoft.JSInterop;

namespace Cockpit;

public partial class MainPage : ContentPage
{
	readonly SplashFeature _splashFeature;
	readonly SessionFeature _sessionFeature;
	readonly ThemeStateFeature _themeStateFeature;
	int _splashHidden;

	public MainPage(
		SplashFeature splashFeature,
		SessionFeature sessionFeature,
		ThemeStateFeature themeStateFeature)
	{
		InitializeComponent();

		_splashFeature = splashFeature;
		_sessionFeature = sessionFeature;
		_themeStateFeature = themeStateFeature;

#if WINDOWS
		ConfigureWindowsContextMenu();
#endif

		_splashFeature.OnBlazorReady += OnBlazorReady;
		_splashFeature.OnStatusUpdate += OnSplashStatusUpdate;
		_splashFeature.OnSplashHide += OnSplashHide;

		// Safety timeout — force-hide splash after 30 seconds in case startup hangs
		Dispatcher.StartTimer(TimeSpan.FromSeconds(30), () =>
		{
			_ = HideSplash();
			return false;
		});
	}

	void OnBlazorReady()
	{
		Dispatcher.Dispatch(() =>
		{
#if DEBUG
			DiagnosticsSettings.OpenLogViewer(_themeStateFeature.IsLightTheme);
#endif
		});
	}

	void OnSplashStatusUpdate(string message)
	{
		Dispatcher.Dispatch(() => statusLabel.Text = message);
	}

	void OnSplashHide()
	{
		Dispatcher.Dispatch(() => _ = HideSplash());
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
	}

	protected override void OnDisappearing()
	{
		base.OnDisappearing();
		_splashFeature.OnBlazorReady -= OnBlazorReady;
		_splashFeature.OnStatusUpdate -= OnSplashStatusUpdate;
		_splashFeature.OnSplashHide -= OnSplashHide;
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
