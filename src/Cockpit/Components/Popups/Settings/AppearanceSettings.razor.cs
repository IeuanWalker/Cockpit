using Cockpit.Features.Theme;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Popups.Settings;

public sealed partial class AppearanceSettings : ComponentBase, IDisposable
{
	string _customColor = "#0078D4";

	readonly IThemeFeature _themeFeature;

	public AppearanceSettings(IThemeFeature themeFeature)
	{
		_themeFeature = themeFeature;
	}

	protected override void OnInitialized()
	{
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
			await _themeFeature.SetAccentColor(colorValue, colorValue);
		}
	}

	async Task SetTheme(ThemeEnum theme)
	{
		await _themeFeature.SetTheme(theme);
	}

	public void Dispose()
	{
		_themeFeature.OnThemeChanged -= OnStateChanged;
		GC.SuppressFinalize(this);
	}
}