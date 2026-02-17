using Cockpit.Services;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Popups;

public enum SettingsSection
{
	Appearance,
	Commands,
	Input,
	Voice,
	Sounds,
	Diagnostics
}

public partial class SettingsPopup : ComponentBase, IDisposable
{


	string _customColor = "#0078D4";
	SettingsSection _activeSection = SettingsSection.Appearance;
	[Inject] UIStateService UIState { get; set; } = default!;
	[Inject] ThemeService ThemeService { get; set; } = default!;

	public void OpenToSection(SettingsSection section)
	{
		_activeSection = section;
		StateHasChanged();
	}

	protected override void OnInitialized()
	{
		UIState.OnStateChanged += OnStateChanged;
		ThemeService.OnThemeChanged += OnStateChanged;
		_customColor = ThemeService.AccentColor;
	}

	protected override void OnAfterRender(bool firstRender)
	{
		if(UIState.PendingSettingsSection.HasValue)
		{
			_activeSection = UIState.PendingSettingsSection.Value;
			UIState.ClearPendingSettingsSection();
			StateHasChanged();
		}
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

	void SetActiveSection(SettingsSection section)
	{
		_activeSection = section;
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
