using Cockpit.Features.Agents;
using Microsoft.JSInterop;

namespace Cockpit;

public partial class MainPage : ContentPage
{
	readonly GlobalAgentFeature _globalAgentFeature;
	public MainPage(GlobalAgentFeature globalAgentFeature)
	{
		InitializeComponent();

		_globalAgentFeature = globalAgentFeature;
	}

	// TODO: Create extened splashscreen and move this logic
	protected override async void OnAppearing()
	{
		await _globalAgentFeature.Load();
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
