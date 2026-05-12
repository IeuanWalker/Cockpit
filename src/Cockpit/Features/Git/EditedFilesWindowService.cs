using Cockpit.Features.Git.Models;
using Cockpit.Features.Splash;
using Cockpit.Features.Theme;

namespace Cockpit.Features.Git;

public sealed class EditedFilesWindowService
{
	readonly ThemeStateFeature _themeStateFeature;
	readonly EditedFilesSplashFeature _splashFeature;

	public EditedFilesWindowService(ThemeStateFeature themeStateFeature, EditedFilesSplashFeature splashFeature)
	{
		_themeStateFeature = themeStateFeature;
		_splashFeature = splashFeature;
	}

	public GitChangedFileModel? PendingInitialFile { get; private set; }

	public event Action<GitChangedFileModel>? OnNavigateToFile;

	public void OpenWindow(GitChangedFileModel? initialFile = null)
	{
		PendingInitialFile = initialFile;

		MainThread.BeginInvokeOnMainThread(() =>
		{
			Window? existing = Application.Current?.Windows.FirstOrDefault(w => w.Page is EditedFilesPage);

			if(existing is not null)
			{
				Application.Current?.ActivateWindow(existing);
				if(initialFile is not null)
				{
					PendingInitialFile = null;
					OnNavigateToFile?.Invoke(initialFile);
				}
				return;
			}

			Application.Current?.OpenWindow(BuildWindow(_themeStateFeature.IsLightTheme, _splashFeature));
		});
	}

	public void ConsumePendingInitialFile() => PendingInitialFile = null;

	static Window BuildWindow(bool isLightTheme, EditedFilesSplashFeature splashFeature)
	{
		Color bg = isLightTheme ? Color.FromArgb("#F8F8F8") : Color.FromArgb("#181818");
		Color fg = isLightTheme ? Color.FromArgb("#3B3B3B") : Color.FromArgb("#CCCCCC");

		return new Window(new EditedFilesPage(splashFeature))
		{
			Title = "Edited Files",
			Width = 1100,
			Height = 720,
			TitleBar = new TitleBar
			{
				BackgroundColor = bg,
				ForegroundColor = fg,
				HeightRequest = 48,
				LeadingContent = new HorizontalStackLayout
				{
					VerticalOptions = LayoutOptions.Center,
					Spacing = 8,
					Margin = new Thickness(10, 0),
					Children =
					{
						new Image
						{
							HeightRequest = 26,
							WidthRequest = 19,
							Source = "logo.png",
							VerticalOptions = LayoutOptions.Center,
						},
						new Label
						{
							Text = "Edited Files",
							TextColor = fg,
							FontSize = 13,
							VerticalOptions = LayoutOptions.Center,
						}
					}
				}
			}
		};
	}
}
