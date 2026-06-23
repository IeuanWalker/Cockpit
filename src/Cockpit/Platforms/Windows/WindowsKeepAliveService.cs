using System.Runtime.InteropServices;

namespace Cockpit.Features.KeepAlive;

sealed partial class WindowsKeepAliveService : IKeepAliveService
{
	const uint es_continuous = 0x80000000;
	const uint es_system_required = 0x00000001;

	[LibraryImport("kernel32.dll")]
	private static partial uint SetThreadExecutionState(uint esFlags);

	public void Activate() => _ = SetThreadExecutionState(es_continuous | es_system_required);

	public void Deactivate() => _ = SetThreadExecutionState(es_continuous);
}
