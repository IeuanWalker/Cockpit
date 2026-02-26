namespace Cockpit.Features.Sessions.Models;

public enum SessionStatusEnum
{
	Idle,
	Active,
	Running,
	NeedsPermission,
	NeedsUserInput,
	Finished,
	Error,
	Archived
}