namespace Cockpit.Features.Sessions.Models;

/// <summary>
/// The agent's lifecycle state, independent of any pending interaction requests.
/// </summary>
public enum AgentRunStateEnum
{
	Idle,
	Running,
	Error
}
