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

	[Fact]
	public void TryTransitionSdkState_AllowsOnlyOneConcurrentOwner()
	{
		SessionLifecycleState lifecycle = new();
		int successfulTransitions = 0;

		Parallel.For(0, 32, _ =>
		{
			if(lifecycle.TryTransitionSdkState(SdkSessionStateEnum.NotLoaded, SdkSessionStateEnum.Loading))
			{
				Interlocked.Increment(ref successfulTransitions);
			}
		});

		successfulTransitions.ShouldBe(1);
		lifecycle.SdkState.ShouldBe(SdkSessionStateEnum.Loading);
	}

	[Fact]
	public void ClearModelChanged_DoesNotDiscardAChangeMadeDuringAsyncWork()
	{
		SessionLifecycleState lifecycle = new();
		lifecycle.MarkModelChanged();
		long handledVersion = lifecycle.CaptureModelChange()!.Value;

		lifecycle.MarkModelChanged();
		lifecycle.ClearModelChanged(handledVersion);

		lifecycle.ModelChanged.ShouldBeTrue();
	}

	[Fact]
	public void SdkTransitionToken_PreventsAStaleLoadFromCompletingANewerLoad()
	{
		SessionLifecycleState lifecycle = new();
		lifecycle.TryBeginSdkTransition(
			SdkSessionStateEnum.NotLoaded,
			SdkSessionStateEnum.Loading,
			out SdkLifecycleTransition staleLoad).ShouldBeTrue();

		lifecycle.SetSdkState(SdkSessionStateEnum.NotLoaded);
		lifecycle.TryBeginSdkTransition(
			SdkSessionStateEnum.NotLoaded,
			SdkSessionStateEnum.Loading,
			out SdkLifecycleTransition currentLoad).ShouldBeTrue();

		lifecycle.TryCompleteLoad(staleLoad).ShouldBeFalse();
		lifecycle.SdkState.ShouldBe(SdkSessionStateEnum.Loading);
		lifecycle.TryCompleteLoad(currentLoad).ShouldBeTrue();
		lifecycle.SdkState.ShouldBe(SdkSessionStateEnum.Loaded);
	}

	[Fact]
	public void CapturedSdkTransition_PreventsAStaleReplacementFromRegistering()
	{
		SessionLifecycleState lifecycle = new();
		lifecycle.SetSdkState(SdkSessionStateEnum.Resumed);
		SdkLifecycleTransition restart = lifecycle.CaptureSdkTransition();
		bool registered = false;

		lifecycle.SetSdkState(SdkSessionStateEnum.NotLoaded);

		lifecycle.TryRunIfSdkTransitionIsCurrent(restart, () => registered = true).ShouldBeFalse();
		registered.ShouldBeFalse();
	}

	[Fact]
	public void CapturedSdkTransition_ResetsCurrentFailedReplacementToNotLoaded()
	{
		SessionLifecycleState lifecycle = new();
		lifecycle.SetSdkState(SdkSessionStateEnum.Resumed);
		SdkLifecycleTransition restart = lifecycle.CaptureSdkTransition();

		lifecycle.TryInvalidateSdkTransition(restart).ShouldBeTrue();
		lifecycle.SdkState.ShouldBe(SdkSessionStateEnum.NotLoaded);
	}

	[Fact]
	public void ResetForEviction_ClearsSdkAndConfigurationStateTogether()
	{
		SessionLifecycleState lifecycle = new();
		lifecycle.SetSdkState(SdkSessionStateEnum.Resumed);
		lifecycle.MarkModelChanged();
		lifecycle.MarkAgentChanged();
		lifecycle.MarkAgentModeChanged();

		lifecycle.ResetForEviction();

		lifecycle.SdkState.ShouldBe(SdkSessionStateEnum.NotLoaded);
		lifecycle.ModelChanged.ShouldBeFalse();
		lifecycle.AgentChanged.ShouldBeFalse();
		lifecycle.AgentModeChanged.ShouldBeFalse();
	}
}
