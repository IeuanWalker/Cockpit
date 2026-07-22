using Cockpit.Features.Sessions.Models;
using GitHub.Copilot;
using Shouldly;

namespace Cockpit.UnitTests.Features.Sessions;

public sealed class SessionLifecycleStateTests
{
	static SessionModel CreateSession() => new()
	{
		Id = "lifecycle-session",
		Title = "Lifecycle state",
		CreatedAt = DateTime.UtcNow,
		LastActivity = DateTime.UtcNow,
		Model = new ModelInfo { Id = "test", Name = "Test Model" },
		Context = new()
		{
			CurrentWorkingDirectory = string.Empty,
			WorkspacePath = null,
			GitRoot = null,
			Repository = null,
			Branch = null
		}
	};

	[Fact]
	public void CompatibilitySurface_ForwardsToLifecycleState()
	{
		SessionModel session = CreateSession();

		session.AgentRunState = AgentRunStateEnum.Running;
		session.SdkState = SdkSessionStateEnum.Resumed;
		session.ModelChanged = true;
		session.AgentChanged = true;
		session.AgentModeChanged = true;
		session.SuppressFinishedNotification = true;

		session.Lifecycle.AgentRunState.ShouldBe(AgentRunStateEnum.Running);
		session.Lifecycle.SdkState.ShouldBe(SdkSessionStateEnum.Resumed);
		session.Lifecycle.ModelChanged.ShouldBeTrue();
		session.Lifecycle.AgentChanged.ShouldBeTrue();
		session.Lifecycle.AgentModeChanged.ShouldBeTrue();
		session.Lifecycle.SuppressFinishedNotification.ShouldBeTrue();
	}

	[Fact]
	public void DisplayStatus_UsesLifecycleRunState()
	{
		SessionModel session = CreateSession();

		session.Lifecycle.AgentRunState = AgentRunStateEnum.Running;

		session.DisplayStatus.ShouldBe(SessionStatusEnum.Running);
		session.Status.ShouldBe(SessionStatusEnum.Running);
	}
}
