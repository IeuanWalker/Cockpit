using Cockpit.Features.Theme;
using Cockpit.Features.UIState;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Popups.Settings;

public partial class AppearanceSettings : ComponentBase, IDisposable
{
	string _customColor = "#0078D4";

	readonly UIStateFeature _uiStateFeature;
	readonly ThemeFeature _themeFeature;
	public AppearanceSettings(UIStateFeature uiStateFeature, ThemeFeature themeFeature)
	{
		_uiStateFeature = uiStateFeature;
		_themeFeature = themeFeature;
	}

	protected override void OnInitialized()
	{
		_uiStateFeature.OnStateChanged += OnStateChanged;
		_themeFeature.OnThemeChanged += OnStateChanged;
		_customColor = _themeFeature.AccentColor;
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
		await _themeFeature.SetTheme(theme);
	}

	async Task SetAccentColor(string color, string hoverColor)
	{
		await _themeFeature.SetAccentColor(color, hoverColor);
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
			_uiStateFeature.OnStateChanged -= OnStateChanged;
			_themeFeature.OnThemeChanged -= OnStateChanged;
		}
	}
}