using Cockpit.Services;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Popups.Settings;

public partial class AppearanceSettings : ComponentBase, IDisposable
{
	string _customColor = "#0078D4";
	[Inject] UIStateService _uiState { get; set; } = default!;
	[Inject] ThemeService _themeService { get; set; } = default!;

	protected override void OnInitialized()
	{
		_uiState.OnStateChanged += OnStateChanged;
		_themeService.OnThemeChanged += OnStateChanged;
		_customColor = _themeService.AccentColor;
	}

	void OnStateChanged()
	{
		InvokeAsync(StateHasChanged);
	}

	async Task OnCustomColorInput(ChangeEventArgs e)
	{
		if(e.Value is string colorValue)
		{
			_customColor = colorValue;
			await SetAccentColor(colorValue, colorValue);
		}
	}

	async Task SetTheme(ThemeEnum theme)
	{
		await _themeService.SetThemeAsync(theme);
	}

	async Task SetAccentColor(string color, string hoverColor)
	{
		await _themeService.SetAccentColorAsync(color, hoverColor);
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
			_themeService.OnThemeChanged -= OnStateChanged;
		}
	}
}