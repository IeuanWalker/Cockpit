using Cockpit.Services;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components;

public partial class SettingsPopup : ComponentBase, IDisposable
{
	string _customColor = "#0078D4";
	[Inject] UIStateService UIState { get; set; } = default!;
	[Inject] ThemeService ThemeService { get; set; } = default!;

	protected override void OnInitialized()
	{
		UIState.OnStateChanged += OnStateChanged;
		ThemeService.OnThemeChanged += OnStateChanged;
		_customColor = ThemeService.AccentColor;
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
			await SetAccentColor(colorValue, colorValue);
		}
	}

	async Task SetTheme(ThemeEnum theme)
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
		if(disposing)
		{
			UIState.OnStateChanged -= OnStateChanged;
			ThemeService.OnThemeChanged -= OnStateChanged;
		}
	}

	class AccentColorOption
	{
		public string Name { get; set; } = string.Empty;
		public string Color { get; set; } = string.Empty;
		public string Hover { get; set; } = string.Empty;
	}
}