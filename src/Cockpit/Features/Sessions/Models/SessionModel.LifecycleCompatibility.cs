namespace Cockpit.Features.Sessions.Models;

/// <summary>
/// Compatibility surface for callers that have not yet adopted <see cref="Lifecycle"/>.
/// Lifecycle and event-processing code should access the grouped state directly.
/// </summary>
public partial class SessionModel
{
	public AgentRunStateEnum AgentRunState
	{
		get => Lifecycle.AgentRunState;
		set => Lifecycle.AgentRunState = value;
	}

	public SdkSessionStateEnum SdkState
	{
		get => Lifecycle.SdkState;
		set => Lifecycle.SdkState = value;
	}

	public bool ModelChanged
	{
		get => Lifecycle.ModelChanged;
		set => Lifecycle.ModelChanged = value;
	}

	public bool AgentChanged
	{
		get => Lifecycle.AgentChanged;
		set => Lifecycle.AgentChanged = value;
	}

	public bool AgentModeChanged
	{
		get => Lifecycle.AgentModeChanged;
		set => Lifecycle.AgentModeChanged = value;
	}

	public bool SuppressFinishedNotification
	{
		get => Lifecycle.SuppressFinishedNotification;
		set => Lifecycle.SuppressFinishedNotification = value;
	}
}
