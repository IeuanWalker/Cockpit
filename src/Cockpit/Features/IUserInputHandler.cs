using GitHub.Copilot.SDK;

namespace Cockpit.Features.UserInputs;

public interface IUserInputHandler
{
	Task<UserInputResponse> HandleUserInputRequest(UserInputRequest request, UserInputInvocation invocation);
}
