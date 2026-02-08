namespace CopilotGUI;

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
			Title = "GitHub Copilot CLI",
			TitleBar = new TitleBar
			{
				Title = "GitHub Copilot CLI",
				BackgroundColor = Color.FromArgb("#1F1F1F"),
				ForegroundColor = Color.FromArgb("#CCCCCC"),
				HeightRequest = 48,
				TrailingContent = new HorizontalStackLayout
				{
					Children =
					{
						new ImageButton
						{
							HeightRequest = 36,
							WidthRequest = 36,
							BorderWidth = 0,
							Background = Colors.Transparent,
							Source = new FontImageSource
							{
								Size = 16,
								Glyph = "&#xE713;",
								FontFamily="SegoeMDL2"
							},
							Command = new Command(OnSettingsClicked)
						}
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

	public static void UpdateTitleBarTheme(string theme)
	{
		App? app = Current as App;
		if(app?._mainWindow?.TitleBar is TitleBar titleBar)
		{
			if(theme == "light")
			{
				titleBar.BackgroundColor = Color.FromArgb("#FFFFFF");
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
				titleBar.BackgroundColor = Color.FromArgb("#1F1F1F");
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
