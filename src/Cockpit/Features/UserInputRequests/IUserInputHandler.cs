using GitHub.Copilot.SDK;

namespace Cockpit.Features.UserInputRequests;

public interface IUserInputHandler
{
	Task<UserInputResponse> HandleUserInputRequest(UserInputRequest request, UserInputInvocation invocation);

	/// <summary>
	/// Cancels all pending user input requests for the specified session (e.g., when the session is deleted).
	/// </summary>
	void CancelPendingRequestsForSession(string sessionId);
}
