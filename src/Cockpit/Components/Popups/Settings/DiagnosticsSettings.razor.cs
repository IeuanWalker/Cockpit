using Cockpit.Utilities.Logging;
using System.Diagnostics;

namespace Cockpit.Components.Popups.Settings;

public partial class DiagnosticsSettings
{
	string LogDirectory => LogDirectoryHelper.LogDirectory;

	void OpenLogFolder()
	{
		try
		{
			if(OperatingSystem.IsWindows())
				Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"\"{LogDirectory}\"", UseShellExecute = true });
			else if(OperatingSystem.IsMacOS())
				Process.Start(new ProcessStartInfo { FileName = "open", Arguments = $"\"{LogDirectory}\"", UseShellExecute = false });
		}
		catch { /* best-effort */ }
	}
}