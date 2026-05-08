using Cockpit.Controls;
using Cockpit.Features.Sessions;
using Cockpit.Features.Splash;
using Cockpit.Features.Theme;

namespace Cockpit;

public partial class App : Application
{
	Window? _mainWindow;

	readonly SplashFeature _splashFeature;
	readonly SessionFeature _sessionFeature;
	readonly ThemeStateFeature _themeStateFeature;

	public App(
		SplashFeature splashFeature,
		SessionFeature sessionFeature,
		ThemeStateFeature themeStateFeature)
	{
		InitializeComponent();

		_splashFeature = splashFeature;
		_sessionFeature = sessionFeature;
		_themeStateFeature = themeStateFeature;
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		_mainWindow = new Window(new MainPage(_splashFeature, _sessionFeature, _themeStateFeature))
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

		_mainWindow.Destroying += OnMainWindowDestroying;
		return _mainWindow;
	}

	async void OnSettingsClicked()
	{
		if(Current?.Windows?.FirstOrDefault()?.Page is MainPage mainPage)
		{
			await mainPage.InvokeJavaScriptAsync("window.toggleSettings?.()");
		}
	}

	void OnMainWindowDestroying(object? sender, EventArgs e)
	{
		List<Window> secondary = Application.Current?.Windows
			.Where(w => w != _mainWindow)
			.ToList() ?? [];

		foreach(Window win in secondary)
		{
			Application.Current?.CloseWindow(win);
		}
	}
}
