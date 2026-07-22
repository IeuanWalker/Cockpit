using Cockpit.Features.ElicitationRequests;
using Cockpit.Features.Permissions.Models;
using Cockpit.Features.Sessions;
using Cockpit.Features.Sessions.Interactions;
using Cockpit.Features.Sessions.Models;
using Cockpit.Features.UserInputRequests;
using GitHub.Copilot;
using Shouldly;

namespace Cockpit.UnitTests.Features.Sessions;

public sealed class SessionInteractionCoordinatorTests
{
	const string sessionId = "interaction-session";
	static readonly ModelInfo testModel = new() { Id = "test", Name = "Test Model" };

	sealed class TestSessionStateProvider : ISessionStateProvider
	{
		readonly List<SessionModel> _sessions = [];

		public event Action? OnStateChanged;
		public IReadOnlyList<SessionModel> Sessions => _sessions;
		public SessionModel? CurrentSession => _sessions.FirstOrDefault();
		public int NotificationCount { get; private set; }

		public void Add(SessionModel session) => _sessions.Add(session);

		public void NotifyStateChanged()
		{
			NotificationCount++;
			OnStateChanged?.Invoke();
		}
	}

	static SessionModel CreateSession() => new()
	{
		Id = sessionId,
		Title = "Interaction coordinator",
		CreatedAt = DateTime.UtcNow,
		LastActivity = DateTime.UtcNow,
		Model = testModel,
		AgentRunState = AgentRunStateEnum.Running,
		Context = new()
		{
			CurrentWorkingDirectory = string.Empty,
			WorkspacePath = null,
			GitRoot = null,
			Repository = null,
			Branch = null
		}
	};

	static PermissionRequestModel CreatePermission(string id = "permission") => new()
	{
		Id = id,
		SessionId = sessionId,
		FullCommand = "command",
		Commands = ["command"],
		RequestTitle = "Allow command",
		Intention = "test",
		CanApproveGlobally = true,
		CanApproveForSession = true,
		FullRequestJson = "{}"
	};

	static UserInputRequestModel CreateUserInput() => new()
	{
		SessionId = sessionId,
		Question = "Continue?",
		Choices = [],
		AllowsTextInput = true,
		FullRequestJson = "{}"
	};

	static ElicitationRequestModel CreateElicitation() => new()
	{
		SessionId = sessionId,
		Message = "Provide details",
		Fields = [],
		ElicitationSource = "test"
	};

	[Fact]
	public void ConcurrentTypes_PreserveRunStateAndExistingDisplayOrder()
	{
		TestSessionStateProvider stateProvider = new();
		SessionModel session = CreateSession();
		stateProvider.Add(session);
		SessionInteractionCoordinator coordinator = new(stateProvider);
		PermissionRequestModel permission = CreatePermission();
		UserInputRequestModel userInput = CreateUserInput();
		ElicitationRequestModel elicitation = CreateElicitation();

		coordinator.AddPermission(sessionId, permission);
		coordinator.AddUserInput(sessionId, userInput);
		coordinator.AddElicitation(sessionId, elicitation);

		session.AgentRunState.ShouldBe(AgentRunStateEnum.Running);
		session.Status.ShouldBe(SessionStatusEnum.NeedsElicitation);

		coordinator.ResolveElicitation(sessionId, elicitation.Id);
		session.Status.ShouldBe(SessionStatusEnum.NeedsPermission);

		coordinator.ResolvePermission(sessionId, permission.Id);
		session.Status.ShouldBe(SessionStatusEnum.NeedsUserInput);

		coordinator.ResolveUserInput(sessionId, userInput.Id);
		session.Status.ShouldBe(SessionStatusEnum.Running);
		session.AgentRunState.ShouldBe(AgentRunStateEnum.Running);
		stateProvider.NotificationCount.ShouldBe(6);
	}

	[Fact]
	public void DuplicateRequest_DoesNotChangeStatusOrNotifyAgain()
	{
		TestSessionStateProvider stateProvider = new();
		SessionModel session = CreateSession();
		stateProvider.Add(session);
		SessionInteractionCoordinator coordinator = new(stateProvider);
		PermissionRequestModel permission = CreatePermission();

		coordinator.AddPermission(sessionId, permission);
		coordinator.AddPermission(sessionId, permission);

		session.PendingInteractions.Permissions.Count.ShouldBe(1);
		session.AgentRunState.ShouldBe(AgentRunStateEnum.Running);
		session.Status.ShouldBe(SessionStatusEnum.NeedsPermission);
		stateProvider.NotificationCount.ShouldBe(1);
	}

	[Fact]
	public void RunStateChangeWhileInteractionIsPending_IsRevealedAfterResolution()
	{
		TestSessionStateProvider stateProvider = new();
		SessionModel session = CreateSession();
		stateProvider.Add(session);
		SessionInteractionCoordinator coordinator = new(stateProvider);
		PermissionRequestModel permission = CreatePermission();

		coordinator.AddPermission(sessionId, permission);
		session.AgentRunState = AgentRunStateEnum.Error;

		session.DisplayStatus.ShouldBe(SessionStatusEnum.NeedsPermission);

		coordinator.ResolvePermission(sessionId, permission.Id);

		session.AgentRunState.ShouldBe(AgentRunStateEnum.Error);
		session.DisplayStatus.ShouldBe(SessionStatusEnum.Error);
	}

	[Fact]
	public void ResolveAfterReconnectCleanup_DoesNotChangeRunStateOrNotify()
	{
		TestSessionStateProvider stateProvider = new();
		SessionModel session = CreateSession();
		stateProvider.Add(session);
		SessionInteractionCoordinator coordinator = new(stateProvider);
		PermissionRequestModel permission = CreatePermission();

		coordinator.AddPermission(sessionId, permission);

		// Reconnect restores the lifecycle status and clears UI bookkeeping before
		// cancellation completes the SDK-facing request.
		session.AgentRunState = AgentRunStateEnum.Running;
		coordinator.ClearBookkeeping(
			sessionId,
			PendingInteractionKinds.All);

		coordinator.ResolvePermission(sessionId, permission.Id);

		session.Status.ShouldBe(SessionStatusEnum.Running);
		session.AgentRunState.ShouldBe(AgentRunStateEnum.Running);
		session.PendingInteractions.Permissions.ShouldBeEmpty();
		stateProvider.NotificationCount.ShouldBe(1);
	}
}
