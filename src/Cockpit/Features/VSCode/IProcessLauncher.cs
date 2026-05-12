using System.Diagnostics;

namespace Cockpit.Features.VSCode;

interface IProcessLauncher
{
	Process? Start(ProcessStartInfo startInfo);
}
