using System.Diagnostics;

namespace Cockpit.Features.VSCode;

sealed class DefaultProcessLauncher : IProcessLauncher
{
	public Process? Start(ProcessStartInfo startInfo) => Process.Start(startInfo);
}
