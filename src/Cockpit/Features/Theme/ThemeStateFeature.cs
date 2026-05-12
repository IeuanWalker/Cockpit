using Cockpit.Features.AppSettings;

namespace Cockpit.Features.Theme;

/// <summary>
/// Singleton that holds the current resolved theme state and notifies all subscribers
/// (including secondary windows with their own BlazorWebView scope).
/// </summary>
public sealed class ThemeStateFeature
{
	public event Action? OnThemeChanged;

	public bool IsLightTheme { get; private set; }
	public string AccentColor { get; private set; }
	public string AccentHoverColor { get; private set; }

	public ThemeStateFeature(IAppSettingsFeature appSettings)
	{
		ThemeEnum theme = appSettings.Theme;
		IsLightTheme = theme == ThemeEnum.Light ||
			(theme == ThemeEnum.System && (Application.Current?.RequestedTheme ?? AppTheme.Dark) == AppTheme.Light);
		AccentColor = appSettings.AccentColor;
		AccentHoverColor = appSettings.AccentHoverColor;
	}

	public void Update(bool isLightTheme, string accentColor, string accentHoverColor)
	{
		IsLightTheme = isLightTheme;
		AccentColor = accentColor;
		AccentHoverColor = accentHoverColor;
		OnThemeChanged?.Invoke();
	}
}
