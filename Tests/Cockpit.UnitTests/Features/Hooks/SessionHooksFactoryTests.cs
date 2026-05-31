using Cockpit.Features.Hooks;
using GitHub.Copilot;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Cockpit.UnitTests.Features.Hooks;

public sealed class SessionHooksFactoryTests
{
	static SessionHooksFactory CreateFactory()
	{
		return new SessionHooksFactory(NullLogger<SessionHooksFactory>.Instance);
	}

	[Fact]
	public void CreateHooks_RegistersExpectedHandlers()
	{
		SessionHooks hooks = CreateFactory().CreateHooks("gpt-5", "high", @"C:\\repo");

		hooks.OnSessionStart.ShouldNotBeNull();
		hooks.OnSessionEnd.ShouldNotBeNull();
		hooks.OnErrorOccurred.ShouldNotBeNull();
	}

	static readonly HookInvocation testInvocation = new() { SessionId = "test-session" };

	[Fact]
	public async Task OnErrorOccurred_RetriesRecoverableErrors()
	{
		SessionHooks hooks = CreateFactory().CreateHooks();
		ErrorOccurredHookOutput? output = await hooks.OnErrorOccurred!(new ErrorOccurredHookInput
		{
			Error = "transient",
			ErrorContext = "model_call",
			Recoverable = true,
			WorkingDirectory = @"C:\\repo"
		}, testInvocation);

		output.ShouldNotBeNull();
		output.ErrorHandling.ShouldBe("retry");
		output.RetryCount.ShouldBe(1);
	}

	[Theory]
	[InlineData("tool_execution")]
	[InlineData("user_input")]
	public async Task OnErrorOccurred_SkipsNonRecoverableInteractiveErrors(string errorContext)
	{
		SessionHooks hooks = CreateFactory().CreateHooks();
		ErrorOccurredHookOutput? output = await hooks.OnErrorOccurred!(new ErrorOccurredHookInput
		{
			Error = "bad input",
			ErrorContext = errorContext,
			Recoverable = false,
			WorkingDirectory = @"C:\\repo"
		}, testInvocation);

		output.ShouldNotBeNull();
		output.ErrorHandling.ShouldBe("skip");
		output.RetryCount.ShouldBeNull();
	}

	[Fact]
	public async Task OnErrorOccurred_AbortsNonRecoverableSystemErrors()
	{
		SessionHooks hooks = CreateFactory().CreateHooks();
		ErrorOccurredHookOutput? output = await hooks.OnErrorOccurred!(new ErrorOccurredHookInput
		{
			Error = "fatal",
			ErrorContext = "system",
			Recoverable = false,
			WorkingDirectory = @"C:\\repo"
		}, testInvocation);

		output.ShouldNotBeNull();
		output.ErrorHandling.ShouldBe("abort");
	}
}
