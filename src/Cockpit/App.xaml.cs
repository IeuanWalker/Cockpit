using Cockpit.Components.Popups.Settings;
using Cockpit.Controls;
using Cockpit.Features.Agents;
using Cockpit.Features.Sessions;
using Cockpit.Features.Splash;
using Cockpit.Features.Theme;

namespace Cockpit;

public partial class App : Application
{
	Window? _mainWindow;

	readonly GlobalAgentFeature _globalAgentFeature;
	readonly SplashFeature _splashFeature;
	readonly SessionFeature _sessionFeature;
	readonly ThemeStateService _themeState;

	public App(GlobalAgentFeature globalAgentFeature, SplashFeature splashFeature, SessionFeature sessionFeature, ThemeStateService themeState)
	{
		InitializeComponent();

		_globalAgentFeature = globalAgentFeature;
		_splashFeature = splashFeature;
		_sessionFeature = sessionFeature;
		_themeState = themeState;
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		_mainWindow = new Window(new MainPage(_globalAgentFeature, _splashFeature, _sessionFeature))
		{
			Title = "Cockpit",
			TitleBar = new TitleBar
			{
				BackgroundColor = Color.FromArgb("#181818"),
				ForegroundColor = Color.FromArgb("#CCCCCC"),
				HeightRequest = 48,
				LeadingContent = new Image
				{
					HeightRequest = 36,
					WidthRequest = 26,
					Margin = new Thickness(10, 0),
					Source = "logo.png"
				},
				TrailingContent = new HorizontalStackLayout
				{
					Children =
					{
						new SponsorButton(),
						new ToolbarIconButton
						{
							Icon = "\uF6A9",
							ClickedCommand = new Command(OnSettingsClicked)
						}
					}
				}
			}
		};

#if DEBUG
		Dispatcher.Dispatch(() => OpenWindow(DiagnosticsSettings.BuildLogViewerWindow(_themeState.IsLightTheme)));
#endif

		return _mainWindow;
	}

	async void OnSettingsClicked()
	{
		if(Current?.Windows?.FirstOrDefault()?.Page is MainPage mainPage)
		{
			await mainPage.InvokeJavaScriptAsync("window.toggleSettings?.()");
		}
	}
}
