using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Cockpit.Features.Theme;

public class ThemeFeature
{
	readonly IJSRuntime _jsRuntime;
	readonly ILogger<ThemeFeature> _logger;
	bool _isInitialized = false;
	bool _isSystemThemeListenerRegistered = false;

	public event Action? OnThemeChanged;

	public ThemeEnum CurrentTheme { get; private set; }
	public string AccentColor { get; private set; }
	public string AccentHoverColor { get; private set; }

	public ThemeFeature(IJSRuntime jsRuntime, ILogger<ThemeFeature> logger)
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
		UpdateTitleBarTheme(CurrentTheme);

		await ApplyThemeAsync();
		await ApplyAccentColorAsync();

		_isInitialized = true;
	}

	public async Task SetThemeAsync(ThemeEnum theme)
	{
		CurrentTheme = theme;
		UserAppSettings.Theme = theme;
		UpdateTitleBarTheme(theme);
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
			UpdateTitleBarTheme(CurrentTheme);
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

	static void UpdateTitleBarTheme(ThemeEnum theme)
	{
		App? app = Application.Current as App;
		bool isLightTheme = theme.Equals(ThemeEnum.Light);

		if(app is not null)
		{
			// Keep MAUI application theme in sync so Windows caption button colors update correctly.
			app.UserAppTheme = theme switch
			{
				ThemeEnum.Light => AppTheme.Light,
				ThemeEnum.Dark => AppTheme.Dark,
				_ => AppTheme.Unspecified
			};

			if(theme.Equals(ThemeEnum.System))
			{
				isLightTheme = app.RequestedTheme.Equals(AppTheme.Light);
			}
		}

		if(app?.Windows[0]?.TitleBar is TitleBar titleBar)
		{
			if(isLightTheme)
			{
				titleBar.BackgroundColor = Color.FromArgb("#F8F8F8");
				titleBar.ForegroundColor = Color.FromArgb("#3B3B3B");

				// Update button text color
				if(titleBar.TrailingContent is HorizontalStackLayout stack &&
					stack.Children.FirstOrDefault() is Button btn)
				{
					btn.TextColor = Color.FromArgb("#3B3B3B");
				}
			}
			else
			{
				titleBar.BackgroundColor = Color.FromArgb("#181818");
				titleBar.ForegroundColor = Color.FromArgb("#CCCCCC");

				// Update button text color
				if(titleBar.TrailingContent is HorizontalStackLayout stack &&
					stack.Children.FirstOrDefault() is Button btn)
				{
					btn.TextColor = Color.FromArgb("#CCCCCC");
				}
			}
		}
	}
}
