using CopilotUIWebAssembly.Services;
using Microsoft.AspNetCore.Components;

namespace CopilotUIWebAssembly.Components;

public partial class SettingsPopup : ComponentBase, IDisposable
{
	readonly List<AccentColorOption> _accentColors =
	[
		new AccentColorOption { Name = "Blue (Default)", Color = "#0078D4", Hover = "#026ec1" },
		new AccentColorOption { Name = "Dark Blue", Color = "#005FB8", Hover = "#0050a0" },
		new AccentColorOption { Name = "Purple", Color = "#8764B8", Hover = "#7555a6" },
		new AccentColorOption { Name = "Magenta", Color = "#C239B3", Hover = "#b02aa0" },
		new AccentColorOption { Name = "Red", Color = "#E74856", Hover = "#d53845" },
		new AccentColorOption { Name = "Orange", Color = "#FF6C2F", Hover = "#e65d28" },
		new AccentColorOption { Name = "Green", Color = "#10893E", Hover = "#0e7836" },
		new AccentColorOption { Name = "Cyan", Color = "#00B7C3", Hover = "#00a3ae" },
		new AccentColorOption { Name = "Bright Red", Color = "#E81123", Hover = "#d00f1f" },
		new AccentColorOption { Name = "Bright Orange", Color = "#F7630C", Hover = "#de580b" },
		new AccentColorOption { Name = "Yellow", Color = "#FFB900", Hover = "#e6a700" },
		new AccentColorOption { Name = "Bright Green", Color = "#00CC6A", Hover = "#00b85f" }
	];

	protected override void OnInitialized()
	{
		UIState.OnStateChanged += StateHasChanged;
		ThemeService.OnThemeChanged += StateHasChanged;
	}

	async Task SetTheme(string theme)
	{
		await ThemeService.SetThemeAsync(theme);
	}

	async Task SetAccentColor(string color, string hoverColor)
	{
		await ThemeService.SetAccentColorAsync(color, hoverColor);
	}

	public void Dispose()
	{
		Dispose(true);
		GC.SuppressFinalize(this);
	}

	protected virtual void Dispose(bool disposing)
	{
		if (disposing)
		{
			UIState.OnStateChanged -= StateHasChanged;
			ThemeService.OnThemeChanged -= StateHasChanged;
		}
	}

	class AccentColorOption
	{
		public string Name { get; set; } = string.Empty;
		public string Color { get; set; } = string.Empty;
		public string Hover { get; set; } = string.Empty;
	}
}