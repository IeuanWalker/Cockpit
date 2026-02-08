using Microsoft.JSInterop;

namespace CopilotGUI.Services;

public class ThemeService
{
	readonly IJSRuntime _jsRuntime;
	readonly LocalStorageService _localStorage;
	bool _isInitialized = false;

	public event Action? OnThemeChanged;

	public string CurrentTheme { get; private set; } = "dark";
	public string AccentColor { get; private set; } = "#0078D4";
	public string AccentHoverColor { get; private set; } = "#026ec1";

	public ThemeService(IJSRuntime jsRuntime, LocalStorageService localStorage)
	{
		_jsRuntime = jsRuntime;
		_localStorage = localStorage;
	}

	public async Task InitializeAsync()
	{
		if(_isInitialized)
		{
			return;
		}

		string? savedTheme = await _localStorage.GetItemAsync("theme");
		CurrentTheme = savedTheme ?? "dark";

		string? savedAccent = await _localStorage.GetItemAsync("accentColor");
		string? savedAccentHover = await _localStorage.GetItemAsync("accentHoverColor");

		if(!string.IsNullOrEmpty(savedAccent) && !string.IsNullOrEmpty(savedAccentHover))
		{
			AccentColor = savedAccent;
			AccentHoverColor = savedAccentHover;
		}
		else
		{
			// Set defaults based on theme
			if(CurrentTheme == "light")
			{
				AccentColor = "#005FB8";
				AccentHoverColor = "#0050a0";
			}
		}

		await ApplyThemeAsync();
		_isInitialized = true;
	}

	public async Task SetThemeAsync(string theme)
	{
		CurrentTheme = theme;
		await _localStorage.SetItemAsync("theme", theme);
		await ApplyThemeAsync();
		OnThemeChanged?.Invoke();
	}

	public async Task SetAccentColorAsync(string color, string hoverColor)
	{
		AccentColor = color;
		AccentHoverColor = hoverColor;
		await _localStorage.SetItemAsync("accentColor", color);
		await _localStorage.SetItemAsync("accentHoverColor", hoverColor);
		await ApplyAccentColorAsync();
		OnThemeChanged?.Invoke();
	}

	async Task ApplyThemeAsync()
	{
		try
		{
			if(CurrentTheme == "light")
			{
				await _jsRuntime.InvokeVoidAsync("copilotUI.addBodyClass", "light-theme");
			}
			else
			{
				await _jsRuntime.InvokeVoidAsync("copilotUI.removeBodyClass", "light-theme");
			}
		}
		catch
		{
			// Handle error silently
		}
	}

	async Task ApplyAccentColorAsync()
	{
		try
		{
			await _jsRuntime.InvokeVoidAsync("copilotUI.setRootProperty", "--accent-color", AccentColor);
			await _jsRuntime.InvokeVoidAsync("copilotUI.setRootProperty", "--button-bg", AccentColor);
			await _jsRuntime.InvokeVoidAsync("copilotUI.setRootProperty", "--button-hover", AccentHoverColor);
		}
		catch
		{
			// Handle error silently
		}
	}
}
