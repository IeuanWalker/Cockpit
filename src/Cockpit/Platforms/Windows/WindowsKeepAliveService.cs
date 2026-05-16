using System.Runtime.InteropServices;

#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace Cockpit.Features.KeepAlive;
#pragma warning restore IDE0130

sealed partial class WindowsKeepAliveService : IKeepAliveService
{
	const uint ES_CONTINUOUS = 0x80000000;
	const uint ES_SYSTEM_REQUIRED = 0x00000001;

	[LibraryImport("kernel32.dll")]
	private static partial uint SetThreadExecutionState(uint esFlags);

	public void Activate() => SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED);

	public void Deactivate() => SetThreadExecutionState(ES_CONTINUOUS);
}
