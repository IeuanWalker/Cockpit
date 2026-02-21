namespace Cockpit.Features.Sessions.Models;

public enum SessionStatusEnum
{
	Idle,
	Active,
	Running,
	NeedsPermission,
	Finished,
	Error,
	Archived
}