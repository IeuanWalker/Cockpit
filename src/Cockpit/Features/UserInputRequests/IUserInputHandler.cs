using GitHub.Copilot.SDK;

namespace Cockpit.Features.UserInputRequests;

public interface IUserInputHandler
{
	Task<UserInputResponse> HandleUserInputRequest(UserInputRequest request, UserInputInvocation invocation);
}
