using Cockpit.Features.Agents;
using Cockpit.Features.Sessions;
using Cockpit.Features.Splash;
using Microsoft.JSInterop;

namespace Cockpit;

public partial class MainPage : ContentPage
{
	readonly GlobalAgentFeature _globalAgentFeature;
	readonly SplashFeature _splashService;
	readonly SessionFeature _sessionFeature;
	int _splashHidden;

	public MainPage(
		GlobalAgentFeature globalAgentFeature,
		SplashFeature splashService,
		SessionFeature sessionFeature)
	{
		InitializeComponent();

		_globalAgentFeature = globalAgentFeature;
		_splashService = splashService;
		_sessionFeature = sessionFeature;

#if WINDOWS
		ConfigureWindowsContextMenu();
#endif

		_splashService.OnBlazorReady += OnBlazorReady;

		// Safety timeout - hide splash after 15 seconds
		Dispatcher.StartTimer(TimeSpan.FromSeconds(15), () =>
		{
			_ = HideSplash();
			return false;
		});
	}

	void OnBlazorReady()
	{
		Dispatcher.Dispatch(() =>
		{
			_ = HideSplash();
		});
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

	protected override async void OnAppearing()
	{
		base.OnAppearing();
#if WINDOWS
		ConfigureWindowsContextMenuOnAppearing();
#endif
		// Fire and forget — starts loading sessions before Blazor is ready
		_ = _sessionFeature.LoadExistingSessions();
		await _globalAgentFeature.Load();
	}

	protected override void OnDisappearing()
	{
		base.OnDisappearing();
		_splashService.OnBlazorReady -= OnBlazorReady;
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
