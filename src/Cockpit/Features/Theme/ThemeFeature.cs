using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Cockpit.Features.Theme;

public class ThemeFeature
{
	readonly IJSRuntime _jsRuntime;
	readonly ILogger<ThemeFeature> _logger;
	bool _isInitialized = false;

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
		UserAppSettings.Theme = theme;

		UpdateTitleBarTheme(theme);
		await ApplyTheme();

		OnThemeChanged?.Invoke();
	}
	async Task ApplyTheme()
	{
		if(Application.Current?.Windows?.FirstOrDefault()?.Page is MainPage mainPage)
		{
			if(GetEffectiveTheme().Equals(ThemeEnum.Light))
			{
				await mainPage.InvokeJavaScriptAsync("window.cockpit?.addBodyClass?.('light-theme');");
			}
			else
			{
				await mainPage.InvokeJavaScriptAsync("window.cockpit?.removeBodyClass?.('light-theme');");
			}

			return;
		}

		if(GetEffectiveTheme().Equals(ThemeEnum.Light))
		{
			await _jsRuntime.InvokeVoidAsync("cockpit.addBodyClass", "light-theme");
		}
		else
		{
			await _jsRuntime.InvokeVoidAsync("cockpit.removeBodyClass", "light-theme");
		}

		ThemeEnum GetEffectiveTheme()
		{
			if(!CurrentTheme.Equals(ThemeEnum.System))
			{
				return CurrentTheme;
			}

			AppTheme requestedTheme = Application.Current?.RequestedTheme ?? AppTheme.Dark;
			return requestedTheme == AppTheme.Light ? ThemeEnum.Light : ThemeEnum.Dark;
		}
	}

	public async Task SetAccentColor(string color, string hoverColor)
	{
		AccentColor = color;
		AccentHoverColor = hoverColor;

		UserAppSettings.AccentColor = color;
		UserAppSettings.AccentHoverColor = hoverColor;

		await ApplyAccentColor();
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

		if(app?.Windows.FirstOrDefault()?.TitleBar is TitleBar titleBar)
		{
			titleBar.BackgroundColor = isLightTheme ? Color.FromArgb("#F8F8F8") : Color.FromArgb("#181818");
			titleBar.ForegroundColor = isLightTheme ? Color.FromArgb("#3B3B3B") : Color.FromArgb("#CCCCCC");

			if(titleBar.TrailingContent is HorizontalStackLayout stack &&
				stack.Children.FirstOrDefault() is Button btn)
			{
				btn.TextColor = isLightTheme ? Color.FromArgb("#3B3B3B") : Color.FromArgb("#CCCCCC");
			}
		}
	}
}
