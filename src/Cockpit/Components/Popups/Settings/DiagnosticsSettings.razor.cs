using Cockpit.Utilities.Logging;
using System.Diagnostics;

namespace Cockpit.Components.Popups.Settings;

public partial class DiagnosticsSettings
{
	string LogDirectory => LogDirectoryHelper.LogDirectory;

	ReportIssuePopup _reportIssuePopup = default!;

	void OpenLogFolder()
	{
		try
		{
			if(OperatingSystem.IsWindows())
			{
				Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"\"{LogDirectory}\"", UseShellExecute = true });
			}
			else if(OperatingSystem.IsMacOS())
			{
				Process.Start(new ProcessStartInfo { FileName = "open", Arguments = $"\"{LogDirectory}\"", UseShellExecute = true });
			}
		}
		catch { /* best-effort */ }
	}

	void OpenReportIssue() => _reportIssuePopup.Open();

	void OpenLogViewer()
	{
		MainThread.BeginInvokeOnMainThread(() =>
		{
			// If a log viewer window is already open, just focus it
			Window? existing = Application.Current?.Windows
				.FirstOrDefault(w => w.Page is LogViewerPage);

			if(existing is not null)
			{
				Application.Current?.ActivateWindow(existing);
				return;
			}

			Application.Current?.OpenWindow(new Window(new LogViewerPage())
			{
				Title = "Log Viewer",
				Width = 960,
				Height = 680,
				TitleBar = new TitleBar
				{
					BackgroundColor = Color.FromArgb("#181818"),
					ForegroundColor = Color.FromArgb("#CCCCCC"),
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
								TextColor = Color.FromArgb("#CCCCCC"),
								FontSize = 13,
								VerticalOptions = LayoutOptions.Center,
							}
						}
					}
				}
			});
		});
	}
}
