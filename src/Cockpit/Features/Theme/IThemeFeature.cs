namespace Cockpit.Features.Theme;

public interface IThemeFeature
{
	event Action? OnThemeChanged;

	ThemeEnum CurrentTheme { get; }
	string AccentColor { get; }
	string AccentHoverColor { get; }
	bool IsLightTheme { get; }

	Task Initialize();
	Task SetTheme(ThemeEnum theme);
	Task SetAccentColor(string color, string hoverColor);
}
