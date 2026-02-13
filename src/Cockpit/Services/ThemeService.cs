using Microsoft.JSInterop;

namespace Cockpit.Services;

public class ThemeService
{
	readonly IJSRuntime _jsRuntime;
	bool _isInitialized = false;

	public event Action? OnThemeChanged;

	public ThemeEnum CurrentTheme { get; private set; }
	public string AccentColor { get; private set; }
	public string AccentHoverColor { get; private set; }

	public ThemeService(IJSRuntime jsRuntime)
	{
		_jsRuntime = jsRuntime;

		CurrentTheme = UserAppSettings.Theme;
		AccentColor = UserAppSettings.AccentColor;
		AccentHoverColor = UserAppSettings.AccentHoverColor;
	}

	public async Task InitializeAsync()
	{
		if(_isInitialized)
		{
			return;
		}

		await ApplyThemeAsync();
		await ApplyAccentColorAsync();

		_isInitialized = true;

		App.UpdateTitleBarTheme(CurrentTheme);
	}

	public async Task SetThemeAsync(ThemeEnum theme)
	{
		CurrentTheme = theme;
		UserAppSettings.Theme = theme;
		await ApplyThemeAsync();
		OnThemeChanged?.Invoke();

		App.UpdateTitleBarTheme(theme);
	}

	public async Task SetAccentColorAsync(string color, string hoverColor)
	{
		AccentColor = color;
		AccentHoverColor = hoverColor;

		UserAppSettings.AccentColor = color;
		UserAppSettings.AccentHoverColor = hoverColor;

		await ApplyAccentColorAsync();
		OnThemeChanged?.Invoke();
	}

	async Task ApplyThemeAsync()
	{
		if(CurrentTheme.Equals(ThemeEnum.Light))
		{
			await _jsRuntime.InvokeVoidAsync("cockpit.addBodyClass", "light-theme");
		}
		else
		{
			await _jsRuntime.InvokeVoidAsync("cockpit.removeBodyClass", "light-theme");
		}
	}

	async Task ApplyAccentColorAsync()
	{
		await _jsRuntime.InvokeVoidAsync("cockpit.setRootProperty", "--accent-color", AccentColor);
		await _jsRuntime.InvokeVoidAsync("cockpit.setRootProperty", "--button-bg", AccentColor);
		await _jsRuntime.InvokeVoidAsync("cockpit.setRootProperty", "--button-hover", AccentHoverColor);
	}
}
