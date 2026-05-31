using System.Text.Json;
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
	string? _htmlContent;
	ElementReference _canvasBodyRef;
	bool _contentChanged;

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

		_htmlContent = ExtractHtmlContent(_instance?.Input);
	}

	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		if(firstRender)
		{
			await ApplyThemeAsync();
			_themeStateFeature.OnThemeChanged += OnThemeChangedHandler;
			_instance?.SplashFeature.NotifyBlazorReady();
			if(_htmlContent is not null)
			{
				await SetCanvasContentAsync();
			}
		}
		else if(_contentChanged)
		{
			_contentChanged = false;
			await SetCanvasContentAsync();
		}
	}

	void OnInstanceChanged(string instanceId)
	{
		if(instanceId != _instanceId)
		{
			return;
		}

		_instance = _windowManager.GetInstance(instanceId);
		_htmlContent = ExtractHtmlContent(_instance?.Input);
		_contentChanged = true;
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

	async Task SetCanvasContentAsync()
	{
		try
		{
			await _jsRuntime.InvokeVoidAsync("cockpit.canvas.setContent", _canvasBodyRef, _htmlContent);
		}
		catch { /* best-effort */ }
	}

	/// <summary>
	/// Extracts the <c>html</c> string from the agent-supplied input JSON.
	/// Returns <see langword="null"/> if the field is absent or not a string.
	/// </summary>
	static string? ExtractHtmlContent(JsonElement? input)
	{
		if(input is null || input.Value.ValueKind != JsonValueKind.Object)
		{
			return null;
		}

		if(input.Value.TryGetProperty("html", out JsonElement htmlProp)
			&& htmlProp.ValueKind == JsonValueKind.String)
		{
			return htmlProp.GetString();
		}

		return null;
	}

	public void Dispose()
	{
		_windowManager.OnInstanceChanged -= OnInstanceChanged;
		_themeStateFeature.OnThemeChanged -= OnThemeChangedHandler;
	}
}
