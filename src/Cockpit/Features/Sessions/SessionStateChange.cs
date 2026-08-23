namespace Cockpit.Features.Sessions;

[Flags]
public enum SessionChangeKind
{
	None = 0,
	ConversationContent = 1 << 0,
	ConversationStructure = 1 << 1,
	SessionSummary = 1 << 2,
	SessionCollection = 1 << 3,
	CurrentSession = 1 << 4,
	/// <summary>
	/// The active working-group shell was created, replaced, or removed.
	/// Content changes within an existing group remain <see cref="ConversationContent"/> only.
	/// </summary>
	WorkingState = 1 << 5,
	/// <summary>
	/// The current conversation was deliberately reset and is being rebuilt. Unlike a
	/// structural shrink, consumers must discard any historical viewport position. This
	/// flag survives notification coalescing with the first rebuilt message.
	/// </summary>
	ConversationReset = 1 << 6,
	/// <summary>
	/// Session context used to classify the session or project changed, such as its working
	/// directory, Git root, or repository identity.
	/// </summary>
	SessionContext = 1 << 7,
	/// <summary>
	/// General invalidation of all renderable state. <see cref="ConversationReset"/> is
	/// intentionally excluded because it describes a specific transition and must never
	/// be inferred from legacy/global "state changed" notifications.
	/// </summary>
	All = ConversationContent | ConversationStructure | SessionSummary | SessionCollection | CurrentSession | WorkingState | SessionContext
}

/// <summary>
/// Describes the part of session state invalidated by a coalesced notification.
/// A null <see cref="SessionId"/> indicates a global or unspecified change.
/// </summary>
public readonly record struct SessionStateChange(string? SessionId, SessionChangeKind Kind);

/// <summary>
/// Shared filtering for components whose rendered state belongs to the selected session.
/// This keeps high-frequency changes from background sessions from invalidating the active UI.
/// </summary>
public static class SessionStateChangeFilter
{
	public static bool IsRelevantToCurrentSession(
		string? currentSessionId,
		SessionStateChange change,
		SessionChangeKind relevantKinds)
	{
		if((change.Kind & relevantKinds) == 0)
		{
			return false;
		}

		// The session id on a CurrentSession notification identifies the newly selected
		// session, so it must be observed even when there was no previous selection.
		if((change.Kind & SessionChangeKind.CurrentSession) != 0)
		{
			return true;
		}

		return change.SessionId is null || string.Equals(change.SessionId, currentSessionId, StringComparison.Ordinal);
	}
}
