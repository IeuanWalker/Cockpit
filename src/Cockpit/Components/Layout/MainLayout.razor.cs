using Cockpit.Components.Popups.Settings;
using Cockpit.Features.Auth;
using Cockpit.Features.Sessions;
using Cockpit.Features.Splash;
using Cockpit.Features.Theme;
using Cockpit.Features.UIState;
using Microsoft.JSInterop;

namespace Cockpit.Components.Layout;

public partial class MainLayout : IDisposable
{
	readonly IUIStateFeature _uiStateFeature;
	readonly SessionListFeature _sessionListFeature;
	readonly IThemeFeature _themeFeature;
	readonly IJSRuntime _jsRuntime;
	readonly SplashFeature _splashFeature;
	readonly AuthCheckFeature _authCheckFeature;

	public MainLayout(
		IUIStateFeature uiStateFeature,
		SessionListFeature sessionListFeature,
		IThemeFeature themeFeature,
		IJSRuntime jsRuntime,
		SplashFeature splashFeature,
		AuthCheckFeature authCheckFeature)
	{
		_uiStateFeature = uiStateFeature;
		_sessionListFeature = sessionListFeature;
		_themeFeature = themeFeature;
		_jsRuntime = jsRuntime;
		_splashFeature = splashFeature;
		_authCheckFeature = authCheckFeature;

		_uiStateFeature.OnStateChanged += OnStateChanged;
		_sessionListFeature.OnStateChanged += OnStateChanged;
		_authCheckFeature.OnStateChanged += OnStateChanged;
	}

	SettingsPopup? _settingsPopup;
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
			_splashFeature.NotifyBlazorReady();

			await _authCheckFeature.CheckAuthAsync();
		}
	}

	void OnStateChanged()
	{
		InvokeAsync(StateHasChanged);
	}

	bool _renderedHasSession;
	bool _renderedLeftCollapsed;
	bool _renderedRightCollapsed;
	AuthState _renderedAuthState;

	protected override bool ShouldRender()
	{
		bool hasSession = _sessionListFeature.CurrentSession is not null;
		bool leftCollapsed = _uiStateFeature.LeftSidebarCollapsed;
		bool rightCollapsed = _uiStateFeature.RightSidebarCollapsed;
		AuthState authState = _authCheckFeature.State;

		if(hasSession == _renderedHasSession &&
		   leftCollapsed == _renderedLeftCollapsed &&
		   rightCollapsed == _renderedRightCollapsed &&
		   authState == _renderedAuthState)
		{
			return false;
		}

		_renderedHasSession = hasSession;
		_renderedLeftCollapsed = leftCollapsed;
		_renderedRightCollapsed = rightCollapsed;
		_renderedAuthState = authState;
		return true;
	}

	[JSInvokable("ToggleSettingsFromTitleBar")]
	public void ToggleSettingsFromTitleBar()
	{
		_settingsPopup?.OpenToSection(SettingsSectionEnum.Appearance);
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
			_authCheckFeature.OnStateChanged -= OnStateChanged;
			_dotNetRef?.Dispose();
		}
	}
}