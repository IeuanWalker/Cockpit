using Cockpit.Features.AppSettings;
using Cockpit.Resources.Styles;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Cockpit.Features.Theme;

public class ThemeFeature
{
	readonly IJSRuntime _jsRuntime;
	readonly ILogger<ThemeFeature> _logger;
	readonly IAppSettingsFeature _appSettings;
	readonly ThemeStateFeature _themeStateFeature;
	bool _isInitialized = false;

	public event Action? OnThemeChanged;

	public ThemeEnum CurrentTheme { get; private set; }
	public string AccentColor { get; private set; }
	public string AccentHoverColor { get; private set; }

	public bool IsLightTheme
	{
		get
		{
			if(CurrentTheme != ThemeEnum.System)
			{
				return CurrentTheme == ThemeEnum.Light;
			}

			return (Application.Current?.RequestedTheme ?? AppTheme.Dark) == AppTheme.Light;
		}
	}

	public ThemeFeature(
		IJSRuntime jsRuntime,
		ILogger<ThemeFeature> logger,
		IAppSettingsFeature appSettings,
		ThemeStateFeature themeStateFeature)
	{
		_jsRuntime = jsRuntime;
		_logger = logger;
		_appSettings = appSettings;
		_themeStateFeature = themeStateFeature;

		CurrentTheme = _appSettings.Theme;
		AccentColor = _appSettings.AccentColor;
		AccentHoverColor = _appSettings.AccentHoverColor;
	}

	public async Task Initialize()
	{
		if(_isInitialized)
		{
			return;
		}

		Application.Current?.RequestedThemeChanged += OnRequestedThemeChanged;

		UpdateTitleBarTheme(CurrentTheme);
		await ApplyTheme();
		await ApplyAccentColor();

		_isInitialized = true;
	}

	public async Task SetTheme(ThemeEnum theme)
	{
		CurrentTheme = theme;
		_appSettings.Theme = theme;

		UpdateTitleBarTheme(theme);
		await ApplyTheme();

		OnThemeChanged?.Invoke();
	}

	async Task ApplyTheme()
	{
		bool isLight = IsLightTheme;

		if(Application.Current?.Windows?.FirstOrDefault()?.Page is MainPage mainPage)
		{
			if(isLight)
			{
				await mainPage.InvokeJavaScriptAsync("window.cockpit?.addBodyClass?.('light-theme');");
			}
			else
			{
				await mainPage.InvokeJavaScriptAsync("window.cockpit?.removeBodyClass?.('light-theme');");
			}
		}
		else
		{
			if(isLight)
			{
				await _jsRuntime.InvokeVoidAsync("cockpit.addBodyClass", "light-theme");
				Application.Current!.Resources = new LightTheme();
			}
			else
			{
				await _jsRuntime.InvokeVoidAsync("cockpit.removeBodyClass", "light-theme");
				Application.Current!.Resources = new DarkTheme();
			}
		}

		_themeStateFeature.Update(isLight, AccentColor, AccentHoverColor);
	}

	public async Task SetAccentColor(string color, string hoverColor)
	{
		AccentColor = color;
		AccentHoverColor = hoverColor;

		_appSettings.AccentColor = color;
		_appSettings.AccentHoverColor = hoverColor;

		await ApplyAccentColor();
		_themeStateFeature.Update(IsLightTheme, AccentColor, AccentHoverColor);
		OnThemeChanged?.Invoke();
	}

	async Task ApplyAccentColor()
	{
		await _jsRuntime.InvokeVoidAsync("cockpit.setAccentColor", AccentColor, AccentHoverColor);
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
			await ApplyTheme();

			OnThemeChanged?.Invoke();
		}
		catch(Exception ex)
		{
			_logger.LogDebug(ex, "Failed to apply system theme change");
		}
	}

	static void UpdateTitleBarTheme(ThemeEnum theme)
	{
		App? app = Application.Current as App;
		bool isLightTheme = theme == ThemeEnum.Light;

		if(app is not null)
		{
			// Keep MAUI application theme in sync so Windows caption button colors update correctly.
			app.UserAppTheme = theme switch
			{
				ThemeEnum.Light => AppTheme.Light,
				ThemeEnum.Dark => AppTheme.Dark,
				_ => AppTheme.Unspecified
			};

			if(theme == ThemeEnum.System)
			{
				isLightTheme = app.RequestedTheme == AppTheme.Light;
			}
		}

		// Update title bar on all open windows
		if(app is not null)
		{
			Color bg = isLightTheme ? Color.FromArgb("#F8F8F8") : Color.FromArgb("#181818");
			Color fg = isLightTheme ? Color.FromArgb("#3B3B3B") : Color.FromArgb("#CCCCCC");

			foreach(Window window in app.Windows)
			{
				if(window.TitleBar is TitleBar titleBar)
				{
					titleBar.BackgroundColor = bg;
					titleBar.ForegroundColor = fg;

					// Also update any Label inside LeadingContent (e.g. "Log Viewer" label)
					if(titleBar.LeadingContent is HorizontalStackLayout hsl)
					{
						foreach(Label label in hsl.Children.OfType<Label>())
						{
							label.TextColor = fg;
						}
					}
				}
			}
		}
	}
}
