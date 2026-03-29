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
				Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"\"{LogDirectory}\"", UseShellExecute = true });
			else if(OperatingSystem.IsMacOS())
				Process.Start(new ProcessStartInfo { FileName = "open", Arguments = $"\"{LogDirectory}\"", UseShellExecute = true });
		}
		catch { /* best-effort */ }
	}

	void OpenReportIssue() => _reportIssuePopup.Open();
}
