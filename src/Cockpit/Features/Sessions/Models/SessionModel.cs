using Cockpit.Features.Sessions.Interactions;
using GitHub.Copilot;

namespace Cockpit.Features.Sessions.Models;

public partial class SessionModel
{
	public required string Id { get; set; }
	public required string Title { get; set; }
	public required DateTime CreatedAt { get; set; }
	public required DateTime LastActivity { get; set; }
	/// <summary>
	/// UI-facing status. The interaction coordinator controls the pending-interaction
	/// overlay while <see cref="Lifecycle"/> continues to track the agent lifecycle.
	/// </summary>
	public SessionStatusEnum DisplayStatus => PendingInteractions.GetDisplayStatus() ?? Lifecycle.AgentRunState switch
	{
		AgentRunStateEnum.Running => SessionStatusEnum.Running,
		AgentRunStateEnum.Error => SessionStatusEnum.Error,
		_ => SessionStatusEnum.Idle
	};

	/// <summary>
	/// Compatibility alias for consumers that only read session status.
	/// New code should use <see cref="SessionLifecycleState.AgentRunState"/> for lifecycle decisions and
	/// <see cref="DisplayStatus"/> for presentation.
	/// </summary>
	public SessionStatusEnum Status => DisplayStatus;

	public PendingInteractionState PendingInteractions { get; } = new();
	public SessionConversationState Conversation { get; } = new();
	public SessionUiState Ui { get; } = new();
	public SessionLifecycleState Lifecycle { get; } = new();
	public required SessionContext Context { get; set; }
	public required ModelInfo Model { get; set; }
	public string? ReasoningEffort { get; set; }

	/// <summary>
	/// When set, identifies the <see cref="ByokModelConfig"/> that provides the active model.
	/// Null for built-in Copilot models.
	/// </summary>
	public string? ByokConfigId { get; set; }
}
