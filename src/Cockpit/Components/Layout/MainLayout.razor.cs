using Cockpit.Components.Popups.Settings;
using Cockpit.Features.Sessions;
using Cockpit.Features.Theme;
using Cockpit.Features.UIState;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Cockpit.Components.Layout;

public partial class MainLayout : IDisposable
{
	readonly UIStateFeature _uiStateFeature;
	readonly SessionListFeature _sessionListFeature;
	readonly ThemeFeature _themeFeature;

	[Inject] IJSRuntime _jsRuntime { get; set; } = default!;

	public MainLayout(
		UIStateFeature uiStateFeature,
		SessionListFeature sessionListFeature,
		ThemeFeature themeFeature)
	{
		_uiStateFeature = uiStateFeature;
		_sessionListFeature = sessionListFeature;
		_themeFeature = themeFeature;

		_uiStateFeature.OnStateChanged += OnStateChanged;
		_sessionListFeature.OnStateChanged += OnStateChanged;
	}

	SettingsPopup _settingsPopup = default!;
	DotNetObjectReference<MainLayout>? _dotNetRef;

	protected override async Task OnInitializedAsync()
	{
		await _themeFeature.Initialize();
	}

	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		if(firstRender)
		{
			_dotNetRef = DotNetObjectReference.Create(this);
			await _jsRuntime.InvokeVoidAsync("cockpit.setMainLayoutRef", _dotNetRef);
		}
	}

	void OnStateChanged()
	{
		InvokeAsync(StateHasChanged);
	}

	[JSInvokable("ToggleSettingsFromTitleBar")]
	public void ToggleSettingsFromTitleBar()
	{
		_settingsPopup.OpenToSection(SettingsSectionEnum.Appearance);
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
			_uiStateFeature.OnStateChanged -= OnStateChanged;
			_sessionListFeature.OnStateChanged -= OnStateChanged;
			_dotNetRef?.Dispose();
		}
	}
}