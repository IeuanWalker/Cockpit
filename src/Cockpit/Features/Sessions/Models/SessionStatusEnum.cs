namespace Cockpit.Features.Sessions.Models;

public enum SessionStatusEnum
{
	Idle,
	Running,
	NeedsPermission,
	NeedsUserInput,
	Error,
}