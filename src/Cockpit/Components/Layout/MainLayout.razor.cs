using Cockpit.Components.Popups.Settings;
using Cockpit.Features.Sessions;
using Cockpit.Features.Theme;
using Cockpit.Features.UIState;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Cockpit.Components.Layout;

public partial class MainLayout : IDisposable
{
	[Inject] UIStateFeature _uiState { get; set; } = default!;
	[Inject] SessionListFeature _sessionManager { get; set; } = default!;
	[Inject] ThemeFeature _themeFeature { get; set; } = default!;

	static UIStateFeature? staticUIState;
	SettingsPopup? _settingsPopup;

	protected override async Task OnInitializedAsync()
	{
		await _themeFeature.Initialize();
		_uiState.OnStateChanged += OnStateChanged;
		_sessionManager.OnStateChanged += OnStateChanged;
		staticUIState = _uiState; // Store static reference for title bar access
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
			_uiState.OnStateChanged -= OnStateChanged;
			_sessionManager.OnStateChanged -= OnStateChanged;
		}
	}
}