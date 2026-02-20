using Cockpit.Components.Popups.Settings;
using Cockpit.Features.Theme;
using Cockpit.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Cockpit.Components.Layout;

public partial class MainLayout : IDisposable
{
	[Inject] UIStateService UIState { get; set; } = default!;
	[Inject] UnifiedSessionManager SessionManager { get; set; } = default!;
	[Inject] ThemeFeature ThemeService { get; set; } = default!;

	static UIStateService? staticUIState;
	SettingsPopup? _settingsPopup;

	protected override async Task OnInitializedAsync()
	{
		await ThemeService.InitializeAsync();
		UIState.OnStateChanged += OnStateChanged;
		SessionManager.OnStateChanged += OnStateChanged;
		staticUIState = UIState; // Store static reference for title bar access
	}

	void OnStateChanged()
	{
		InvokeAsync(StateHasChanged);
	}

	[JSInvokable("ToggleSettingsFromTitleBar")]
	public static void ToggleSettingsFromTitleBar()
	{
		staticUIState?.ToggleSettingsPopup();
	}

	public void Dispose()
	{
		Dispose(true);
		GC.SuppressFinalize(this);
	}

	protected virtual void Dispose(bool disposing)
	{
		if(disposing)
		{
			UIState.OnStateChanged -= OnStateChanged;
			SessionManager.OnStateChanged -= OnStateChanged;
		}
	}
}