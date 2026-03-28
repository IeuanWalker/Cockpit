using Cockpit.Controls;
using Cockpit.Features.Agents;

namespace Cockpit;

public partial class App : Application
{
	Window? _mainWindow;

	readonly GlobalAgentFeature _globalAgentFeature;

	public App(GlobalAgentFeature globalAgentFeature)
	{
		InitializeComponent();

		_globalAgentFeature = globalAgentFeature;
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		_mainWindow = new Window(new MainPage(_globalAgentFeature))
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
