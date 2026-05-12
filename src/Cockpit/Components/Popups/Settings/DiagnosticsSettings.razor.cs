using Cockpit.Features.Splash;
using Cockpit.Features.Telemetry;
using Cockpit.Features.Theme;
using Cockpit.Utilities;
using Cockpit.Utilities.Logging;

namespace Cockpit.Components.Popups.Settings;

public partial class DiagnosticsSettings
{
	readonly ThemeStateFeature _themeStateFeature;
	readonly UserAppSettings _userAppSettings;
	readonly TelemetryFileService _telemetryFileService;

	public DiagnosticsSettings(ThemeStateFeature themeStateFeature, UserAppSettings userAppSettings, IServiceProvider serviceProvider)
	{
		_themeStateFeature = themeStateFeature;
		_userAppSettings = userAppSettings;
		_telemetryFileService = serviceProvider.GetRequiredService<TelemetryFileService>();
	}

	string LogDirectory => LogDirectoryHelper.LogDirectory;
	string TelemetryDirectory => _telemetryFileService.TelemetryDirectory;

	ReportIssuePopup _reportIssuePopup = default!;

	void OpenLogFolder()
	{
		FileUtil.RevealFolder(LogDirectory);
	}

	void OpenTelemetryFolder()
	{
		Directory.CreateDirectory(TelemetryDirectory);
		FileUtil.RevealFolder(TelemetryDirectory);
	}

	void OpenReportIssue() => _reportIssuePopup.Open();

	void OpenLogViewer()
	{
		OpenLogViewer(_themeStateFeature.IsLightTheme);
	}

	void OpenTelemetryDashboard()
	{
		OpenTelemetryDashboard(_themeStateFeature.IsLightTheme);
	}

	internal static void OpenLogViewer(bool isLightTheme)
	{
		MainThread.BeginInvokeOnMainThread(() =>
		{
			Window? existing = Application.Current?.Windows.FirstOrDefault(w => w.Page is LogViewerPage);

			if(existing is not null)
			{
				Application.Current?.ActivateWindow(existing);
				return;
			}

			LogViewerSplashFeature splashFeature = IPlatformApplication.Current!.Services.GetRequiredService<LogViewerSplashFeature>();
			Application.Current?.OpenWindow(BuildLogViewerWindow(isLightTheme, splashFeature));
		});
	}

	internal static void OpenTelemetryDashboard(bool isLightTheme)
	{
		MainThread.BeginInvokeOnMainThread(() =>
		{
			Window? existing = Application.Current?.Windows.FirstOrDefault(w => w.Page is TelemetryDashboardPage);

			if(existing is not null)
			{
				Application.Current?.ActivateWindow(existing);
				return;
			}

			TelemetryDashboardSplashFeature splashFeature = IPlatformApplication.Current!.Services.GetRequiredService<TelemetryDashboardSplashFeature>();
			Application.Current?.OpenWindow(BuildTelemetryDashboardWindow(isLightTheme, splashFeature));
		});
	}

	internal static Window BuildLogViewerWindow(bool isLightTheme, LogViewerSplashFeature splashFeature)
	{
		Color bg = isLightTheme ? Color.FromArgb("#F8F8F8") : Color.FromArgb("#181818");
		Color fg = isLightTheme ? Color.FromArgb("#3B3B3B") : Color.FromArgb("#CCCCCC");

		return new Window(new LogViewerPage(splashFeature))
		{
			Title = "Log Viewer",
			Width = 960,
			Height = 680,
			TitleBar = BuildWindowTitleBar(fg, bg, "Log Viewer")
		};
	}

	internal static Window BuildTelemetryDashboardWindow(bool isLightTheme, TelemetryDashboardSplashFeature splashFeature)
	{
		Color bg = isLightTheme ? Color.FromArgb("#F8F8F8") : Color.FromArgb("#181818");
		Color fg = isLightTheme ? Color.FromArgb("#3B3B3B") : Color.FromArgb("#CCCCCC");

		return new Window(new TelemetryDashboardPage(splashFeature))
		{
			Title = "Telemetry Dashboard",
			Width = 1180,
			Height = 760,
			TitleBar = BuildWindowTitleBar(fg, bg, "Telemetry Dashboard")
		};
	}

	static TitleBar BuildWindowTitleBar(Color foreground, Color background, string title)
	{
		return new TitleBar
		{
			BackgroundColor = background,
			ForegroundColor = foreground,
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
						Text = title,
						TextColor = foreground,
						FontSize = 13,
						VerticalOptions = LayoutOptions.Center,
					}
				}
			}
		};
	}
}
