namespace Cockpit.Features.Sessions.Models;

/// <summary>
/// UI-facing session status projected from the agent run state and pending interactions.
/// </summary>
public enum SessionStatusEnum
{
	Idle,
	Running,
	NeedsPermission,
	NeedsUserInput,
	NeedsElicitation,
	Error,
}
