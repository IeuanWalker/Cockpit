using GitHub.Copilot;

namespace Cockpit.Features.ElicitationRequests;

public interface IElicitationHandler
{
	Task<ElicitationResult> HandleElicitationRequest(ElicitationContext context);

	/// <summary>
	/// Cancels all pending elicitation requests for the specified session (e.g., when the session is deleted or disconnected).
	/// </summary>
	void CancelPendingRequestsForSession(string sessionId);
}
