using Cockpit.Features.AppSettings;
using Cockpit.Resources.Styles;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Cockpit.Features.Theme;

public sealed class ThemeFeature : IThemeFeature, IDisposable
{
	readonly IJSRuntime _jsRuntime;
	readonly ILogger<ThemeFeature> _logger;
	readonly IAppSettingsFeature _appSettings;
	readonly ThemeStateFeature _themeStateFeature;
	bool _isInitialized;

	public event Action? OnThemeChanged;

	public ThemeEnum CurrentTheme { get; private set; }
	public string AccentColor { get; private set; }
	public string AccentHoverColor { get; private set; }

	public bool IsLightTheme =>
		CurrentTheme != ThemeEnum.System
			? CurrentTheme == ThemeEnum.Light
			: (Application.Current?.RequestedTheme ?? AppTheme.Dark) == AppTheme.Light;

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

		// Keep the MAUI resource dictionary in sync so native controls always reflect the theme.
		// This replaces the entire ResourceDictionary, which is safe because App.xaml contains
		// no non-theme resources — LightTheme/DarkTheme are self-contained resource dictionaries.
		if(Application.Current is not null)
		{
			Application.Current.Resources = isLight ? new LightTheme() : new DarkTheme();
		}

		// Apply the CSS theme class to the Blazor web layer.
		if(Application.Current?.Windows?.FirstOrDefault()?.Page is MainPage mainPage)
		{
			// Called from outside the primary Blazor scope – dispatch through the native WebView API.
			string js = isLight
				? "window.cockpit?.addBodyClass?.('light-theme');"
				: "window.cockpit?.removeBodyClass?.('light-theme');";
			await mainPage.InvokeJavaScriptAsync(js);
		}
		else
		{
			// Called from within the primary Blazor scope – use the injected JSRuntime.
			await _jsRuntime.InvokeVoidAsync(
				isLight ? "cockpit.addBodyClass" : "cockpit.removeBodyClass",
				"light-theme");
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

	public void Dispose()
	{
		if(Application.Current is not null)
		{
			Application.Current.RequestedThemeChanged -= OnRequestedThemeChanged;
		}
	}

	async void OnRequestedThemeChanged(object? sender, AppThemeChangedEventArgs e)
	{
		if(CurrentTheme != ThemeEnum.System)
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
		if(Application.Current is not App app)
		{
			return;
		}

		// Keep MAUI application theme in sync so Windows caption button colors update correctly.
		app.UserAppTheme = theme switch
		{
			ThemeEnum.Light => AppTheme.Light,
			ThemeEnum.Dark => AppTheme.Dark,
			_ => AppTheme.Unspecified
		};

		bool isLight = theme == ThemeEnum.Light ||
			(theme == ThemeEnum.System && app.RequestedTheme == AppTheme.Light);

		Color bg = isLight ? Color.FromArgb("#F8F8F8") : Color.FromArgb("#181818");
		Color fg = isLight ? Color.FromArgb("#3B3B3B") : Color.FromArgb("#CCCCCC");

		foreach(Window window in app.Windows)
		{
			if(window.TitleBar is TitleBar titleBar)
			{
				titleBar.BackgroundColor = bg;
				titleBar.ForegroundColor = fg;

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
