using Cockpit.Features.Theme;
using Cockpit.Utilities;
using Cockpit.Utilities.Logging;

namespace Cockpit.Components.Popups.Settings;

public partial class DiagnosticsSettings
{
	readonly ThemeStateFeature _themeStateFeature;

	public DiagnosticsSettings(ThemeStateFeature themeStateFeature)
	{
		_themeStateFeature = themeStateFeature;
	}

	string LogDirectory => LogDirectoryHelper.LogDirectory;

	ReportIssuePopup _reportIssuePopup = default!;

	void OpenLogFolder()
	{
		FileUtil.RevealFolder(LogDirectory);
	}

	void OpenReportIssue() => _reportIssuePopup.Open();

	void OpenLogViewer()
	{
		OpenLogViewer(_themeStateFeature.IsLightTheme);
	}

	internal static void OpenLogViewer(bool isLightTheme)
	{
		MainThread.BeginInvokeOnMainThread(() =>
		{
			// If a log viewer window is already open, just focus it
			Window? existing = Application.Current?.Windows.FirstOrDefault(w => w.Page is LogViewerPage);

			if(existing is not null)
			{
				Application.Current?.ActivateWindow(existing);
				return;
			}

			Application.Current?.OpenWindow(BuildLogViewerWindow(isLightTheme));
		});
	}

	internal static Window BuildLogViewerWindow(bool isLightTheme)
	{
		Color bg = isLightTheme ? Color.FromArgb("#F8F8F8") : Color.FromArgb("#181818");
		Color fg = isLightTheme ? Color.FromArgb("#3B3B3B") : Color.FromArgb("#CCCCCC");

		return new Window(new LogViewerPage())
		{
			Title = "Log Viewer",
			Width = 960,
			Height = 680,
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
							Text = "Log Viewer",
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
