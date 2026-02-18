namespace Cockpit;

public partial class App : Application
{
	Window? _mainWindow;

	public App()
	{
		InitializeComponent();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		_mainWindow = new Window(new MainPage())
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
						new Button
						{
							Text = "Settings",
							FontSize = 14,
							Background = Colors.Transparent,
							BorderWidth = 0,
							TextColor = Color.FromArgb("#CCCCCC"),
							Command = new Command(OnSettingsClicked)
						},
					}
				}
			}
		};

		return _mainWindow;
	}

	async void OnSettingsClicked()
	{
		if(Current?.Windows?.FirstOrDefault()?.Page is MainPage mainPage)
		{
			await mainPage.InvokeJavaScriptAsync("window.toggleSettings?.()");
		}
	}

	public static void UpdateTitleBarTheme(ThemeEnum theme)
	{
		App? app = Current as App;
		bool isLightTheme = theme.Equals(ThemeEnum.Light);

		if(app is not null)
		{
			// Keep MAUI application theme in sync so Windows caption button colors update correctly.
			app.UserAppTheme = theme switch
			{
				ThemeEnum.Light => AppTheme.Light,
				ThemeEnum.Dark => AppTheme.Dark,
				_ => AppTheme.Unspecified
			};

			if(theme.Equals(ThemeEnum.System))
			{
				isLightTheme = app.RequestedTheme.Equals(AppTheme.Light);
			}
		}

		if(app?._mainWindow?.TitleBar is TitleBar titleBar)
		{
			if(isLightTheme)
			{
				titleBar.BackgroundColor = Color.FromArgb("#F8F8F8");
				titleBar.ForegroundColor = Color.FromArgb("#3B3B3B");

				// Update button text color
				if(titleBar.TrailingContent is HorizontalStackLayout stack &&
					stack.Children.FirstOrDefault() is Button btn)
				{
					btn.TextColor = Color.FromArgb("#3B3B3B");
				}
			}
			else
			{
				titleBar.BackgroundColor = Color.FromArgb("#181818");
				titleBar.ForegroundColor = Color.FromArgb("#CCCCCC");

				// Update button text color
				if(titleBar.TrailingContent is HorizontalStackLayout stack &&
					stack.Children.FirstOrDefault() is Button btn)
				{
					btn.TextColor = Color.FromArgb("#CCCCCC");
				}
			}
		}
	}
}
