using Microsoft.JSInterop;
using Microsoft.Extensions.Logging;

namespace Cockpit.Services;

public class ThemeService
{
	readonly IJSRuntime _jsRuntime;
	readonly ILogger<ThemeService> _logger;
	bool _isInitialized = false;
	bool _isSystemThemeListenerRegistered = false;

	public event Action? OnThemeChanged;

	public ThemeEnum CurrentTheme { get; private set; }
	public string AccentColor { get; private set; }
	public string AccentHoverColor { get; private set; }

	public ThemeService(IJSRuntime jsRuntime, ILogger<ThemeService> logger)
	{
		_jsRuntime = jsRuntime;
		_logger = logger;

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

		RegisterSystemThemeListener();
		App.UpdateTitleBarTheme(CurrentTheme);

		await ApplyThemeAsync();
		await ApplyAccentColorAsync();

		_isInitialized = true;
	}

	public async Task SetThemeAsync(ThemeEnum theme)
	{
		CurrentTheme = theme;
		UserAppSettings.Theme = theme;
		App.UpdateTitleBarTheme(theme);
		await ApplyThemeAsync();
		OnThemeChanged?.Invoke();
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
		if(GetEffectiveTheme().Equals(ThemeEnum.Light))
		{
			await _jsRuntime.InvokeVoidAsync("cockpit.addBodyClass", "light-theme");
		}
		else
		{
			await _jsRuntime.InvokeVoidAsync("cockpit.removeBodyClass", "light-theme");
		}
	}

	void RegisterSystemThemeListener()
	{
		if(_isSystemThemeListenerRegistered || Application.Current is null)
		{
			return;
		}

		Application.Current.RequestedThemeChanged += OnRequestedThemeChanged;
		_isSystemThemeListenerRegistered = true;
	}

	async void OnRequestedThemeChanged(object? sender, AppThemeChangedEventArgs e)
	{
		if(!CurrentTheme.Equals(ThemeEnum.System))
		{
			return;
		}

		try
		{
			App.UpdateTitleBarTheme(CurrentTheme);
			await ApplyThemeForSystemThemeChangeAsync();
			OnThemeChanged?.Invoke();
		}
		catch(Exception ex)
		{
			_logger.LogDebug(ex, "Failed to apply system theme change");
		}
	}

	ThemeEnum GetEffectiveTheme()
	{
		if(!CurrentTheme.Equals(ThemeEnum.System))
		{
			return CurrentTheme;
		}

		AppTheme requestedTheme = Application.Current?.RequestedTheme ?? AppTheme.Dark;
		return requestedTheme.Equals(AppTheme.Light) ? ThemeEnum.Light : ThemeEnum.Dark;
	}

	async Task ApplyThemeForSystemThemeChangeAsync()
	{
		if(Application.Current?.Windows?.FirstOrDefault()?.Page is MainPage mainPage)
		{
			string script = GetEffectiveTheme().Equals(ThemeEnum.Light)
				? "window.cockpit?.addBodyClass?.('light-theme');"
				: "window.cockpit?.removeBodyClass?.('light-theme');";
			await mainPage.InvokeJavaScriptAsync(script);
			return;
		}

		await ApplyThemeAsync();
	}

	async Task ApplyAccentColorAsync()
	{
		await _jsRuntime.InvokeVoidAsync("cockpit.setRootProperty", "--accent-color", AccentColor);
		await _jsRuntime.InvokeVoidAsync("cockpit.setRootProperty", "--button-bg", AccentColor);
		await _jsRuntime.InvokeVoidAsync("cockpit.setRootProperty", "--button-hover", AccentHoverColor);
	}
}
