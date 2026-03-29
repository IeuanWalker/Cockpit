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

	public MainPage(GlobalAgentFeature globalAgentFeature, SplashFeature splashService, SessionFeature sessionFeature)
	{
		InitializeComponent();

		_globalAgentFeature = globalAgentFeature;
		_splashService = splashService;
		_sessionFeature = sessionFeature;

		_splashService.OnBlazorReady += OnBlazorReady;

		// Safety timeout - hide splash after 15 seconds
		Dispatcher.StartTimer(TimeSpan.FromSeconds(15), () =>
		{
			if(splashOverlay.Opacity > 0)
			{
				HideSplash();
			}

			return false;
		});
	}

	void OnBlazorReady()
	{
		Dispatcher.Dispatch(() => HideSplash());
	}

	async void HideSplash()
	{
		await splashOverlay.FadeToAsync(0, 400, Easing.CubicOut);
		splashOverlay.IsVisible = false;
		// Remove from tree so it cannot intercept input (WinUI hidden views can block scroll)
		((Grid)Content).Children.Remove(splashOverlay);
		blazorWebView.Focus();
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();
		// Fire and forget — starts loading sessions before Blazor is ready
		_ = _sessionFeature.LoadExistingSessions();
		await _globalAgentFeature.Load();
	}

	protected override void OnDisappearing()
	{
		base.OnDisappearing();
		_splashService.OnBlazorReady -= OnBlazorReady;
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