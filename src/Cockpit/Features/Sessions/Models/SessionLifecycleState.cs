namespace Cockpit.Features.Sessions.Models;

/// <summary>
/// State that describes the SDK connection and agent lifecycle for a session.
/// Pending interactions and UI state must not overwrite these values.
/// </summary>
public sealed class SessionLifecycleState
{
	public AgentRunStateEnum AgentRunState { get; set; } = AgentRunStateEnum.Idle;
	public SdkSessionStateEnum SdkState { get; set; } = SdkSessionStateEnum.NotLoaded;

	public bool ModelChanged { get; set; }
	public bool AgentChanged { get; set; }
	public bool AgentModeChanged { get; set; }

	/// <summary>
	/// Prevents history replay from raising a session-finished notification.
	/// </summary>
	public bool SuppressFinishedNotification { get; set; }
}
