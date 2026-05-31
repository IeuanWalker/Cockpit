using Cockpit.Features.Canvas;
using Cockpit.Features.Theme;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Cockpit.Components;

public sealed partial class CanvasRoot : ComponentBase, IDisposable
{
	readonly CanvasWindowManager _windowManager;
	readonly ThemeStateFeature _themeStateFeature;
	readonly IJSRuntime _jsRuntime;

	public CanvasRoot(
		CanvasWindowManager windowManager,
		ThemeStateFeature themeStateFeature,
		IJSRuntime jsRuntime)
	{
		_windowManager = windowManager;
		_themeStateFeature = themeStateFeature;
		_jsRuntime = jsRuntime;
	}

	string? _instanceId;
	CanvasInstanceModel? _instance;

	protected override void OnInitialized()
	{
		// Skip any stale IDs whose instances were removed (e.g. due to a window-open failure).
		do
		{
			_instanceId = _windowManager.ClaimPendingInstanceId();
			if(_instanceId is null)
			{
				break;
			}

			_instance = _windowManager.GetInstance(_instanceId);
		}
		while(_instance is null);

		if(_instanceId is not null)
		{
			_windowManager.OnInstanceChanged += OnInstanceChanged;
		}
	}

	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		if(firstRender)
		{
			await ApplyThemeAsync();
			_themeStateFeature.OnThemeChanged += OnThemeChangedHandler;
			_instance?.SplashFeature.NotifyBlazorReady();
		}
	}

	void OnInstanceChanged(string instanceId)
	{
		if(instanceId != _instanceId)
		{
			return;
		}

		_instance = _windowManager.GetInstance(instanceId);
		InvokeAsync(StateHasChanged);
	}

	void OnThemeChangedHandler() => _ = ApplyThemeAsync();

	async Task ApplyThemeAsync()
	{
		try
		{
			if(_themeStateFeature.IsLightTheme)
			{
				await _jsRuntime.InvokeVoidAsync("cockpit.addBodyClass", "light-theme");
			}
			else
			{
				await _jsRuntime.InvokeVoidAsync("cockpit.removeBodyClass", "light-theme");
			}

			await _jsRuntime.InvokeVoidAsync("cockpit.setAccentColor", _themeStateFeature.AccentColor, _themeStateFeature.AccentHoverColor);
		}
		catch { /* best-effort */ }
	}

	public void Dispose()
	{
		_windowManager.OnInstanceChanged -= OnInstanceChanged;
		_themeStateFeature.OnThemeChanged -= OnThemeChangedHandler;
	}
}
