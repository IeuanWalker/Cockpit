using Cockpit.Components.Controls;
using Cockpit.Features.ElicitationRequests;
using Cockpit.Features.Permissions;
using Cockpit.Features.Permissions.Models;
using Cockpit.Features.Sessions;
using Cockpit.Features.Sessions.Interactions;
using Cockpit.Features.Sessions.Models;
using Cockpit.Features.UserInputRequests;
using GitHub.Copilot;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Cockpit.UnitTests.Features.Sessions;

public sealed class SessionStatusBehaviourTests
{
	const string sessionId = "status-session";
	static readonly ModelInfo testModel = new() { Id = "test", Name = "Test Model" };

	sealed class TestSessionStateProvider : ISessionStateProvider
	{
		readonly List<SessionModel> _sessions = [];

		public event Action? OnStateChanged;
		public IReadOnlyList<SessionModel> Sessions => _sessions;
		public SessionModel? CurrentSession => _sessions.FirstOrDefault();

		public void Add(SessionModel session) => _sessions.Add(session);
		public void NotifyStateChanged() => OnStateChanged?.Invoke();
	}

	static SessionModel CreateRunningSession() => new()
	{
		Id = sessionId,
		Title = "Status behaviour",
		CreatedAt = DateTime.UtcNow,
		LastActivity = DateTime.UtcNow,
		Model = testModel,
		Status = SessionStatusEnum.Running,
		Context = new()
		{
			CurrentWorkingDirectory = string.Empty,
			WorkspacePath = null,
			GitRoot = null,
			Repository = null,
			Branch = null
		}
	};

	[Theory]
	[InlineData(SessionStatusEnum.Idle, "Idle", "idle", "secondary-text")]
	[InlineData(SessionStatusEnum.Running, "Running", "running", "status-running")]
	[InlineData(SessionStatusEnum.Error, "Error", "error", "secondary-text")]
	[InlineData(SessionStatusEnum.NeedsPermission, "Permission required", "needspermission", "status-needs-permission")]
	[InlineData(SessionStatusEnum.NeedsUserInput, "Input requested", "needsuserinput", "status-needs-user-input")]
	[InlineData(SessionStatusEnum.NeedsElicitation, "Input requested", "needselicitation", "status-needs-user-input")]
	public void Presentation_PreservesCurrentLabelsAndClasses(
		SessionStatusEnum status,
		string headerText,
		string headerClass,
		string listClass)
	{
		SessionStatusPresentation.GetHeaderText(status).ShouldBe(headerText);
		SessionStatusPresentation.GetHeaderClass(status).ShouldBe(headerClass);
		SessionStatusPresentation.GetListClass(status).ShouldBe(listClass);
	}

	[Fact]
	public async Task ConcurrentInteractionTypes_PreserveCurrentVisibleStatusTransitions()
	{
		TestSessionStateProvider stateProvider = new();
		SessionModel session = CreateRunningSession();
		stateProvider.Add(session);

		string permissionsPath = Path.Combine(Path.GetTempPath(), $"phase1-permissions-{Guid.NewGuid()}.json");
		string denyPath = Path.Combine(Path.GetTempPath(), $"phase1-deny-{Guid.NewGuid()}.json");
		SessionPermissionFeature sessionPermissions = new(stateProvider);
		SessionInteractionCoordinator interactionCoordinator = new(stateProvider);
		PermissionFeature permissionFeature = new(
			new GlobalPermissionFeature(NullLogger<GlobalPermissionFeature>.Instance, permissionsPath),
			new GlobalDenyFeature(NullLogger<GlobalDenyFeature>.Instance, denyPath),
			sessionPermissions,
			stateProvider,
			NullLogger<PermissionFeature>.Instance,
			interactionCoordinator);
		UserInputFeature userInputFeature = new(stateProvider, NullLogger<UserInputFeature>.Instance, interactionCoordinator);
		ElicitationFeature elicitationFeature = new(stateProvider, NullLogger<ElicitationFeature>.Instance, interactionCoordinator);

		TaskCompletionSource<PermissionRequestModel> permissionReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
		permissionFeature.OnPermissionRequested += (_, request) => permissionReady.TrySetResult(request);

		PermissionRequestModel permissionRequest = new()
		{
			SessionId = sessionId,
			FullCommand = "dangerous-phase1-command",
			Commands = ["dangerous-phase1-command"],
			RequestTitle = "Allow command",
			Intention = "Characterize concurrent status behaviour",
			CanApproveGlobally = true,
			CanApproveForSession = true,
			IsDestructive = true,
			FullRequestJson = "{}"
		};

		Task<PermissionDecisionEnum> permissionTask = permissionFeature.CheckPermissionAsync(permissionRequest);
		await permissionReady.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
		session.Status.ShouldBe(SessionStatusEnum.NeedsPermission);

		TaskCompletionSource<UserInputRequestModel> userInputReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
		userInputFeature.OnUserInputRequested += (_, request) => userInputReady.TrySetResult(request);
		Task<UserInputResponse> userInputTask = userInputFeature.HandleUserInputRequest(
			new UserInputRequest { Question = "Continue?", Choices = [], AllowFreeform = true },
			new UserInputInvocation { SessionId = sessionId });
		UserInputRequestModel userInputRequest = await userInputReady.Task
			.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
		session.Status.ShouldBe(SessionStatusEnum.NeedsUserInput);

		TaskCompletionSource<ElicitationRequestModel> elicitationReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
		elicitationFeature.OnElicitationRequested += (_, request) => elicitationReady.TrySetResult(request);
		Task<ElicitationResult> elicitationTask = elicitationFeature.HandleElicitationRequest(new ElicitationContext
		{
			SessionId = sessionId,
			Message = "Provide details",
			ElicitationSource = "phase1-test"
		});
		ElicitationRequestModel elicitationRequest = await elicitationReady.Task
			.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
		session.Status.ShouldBe(SessionStatusEnum.NeedsElicitation);

		// The existing resolution rules prioritise permission, then user input, before
		// restoring the run state saved when the first blocking request arrived.
		elicitationFeature.ResolveElicitationRequest(elicitationRequest.Id, null);
		await elicitationTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
		session.Status.ShouldBe(SessionStatusEnum.NeedsPermission);

		permissionFeature.ResolvePermissionRequest(permissionRequest.Id, PermissionDecisionEnum.Denied);
		await permissionTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
		session.Status.ShouldBe(SessionStatusEnum.NeedsUserInput);

		userInputFeature.ResolveUserInputRequest(userInputRequest.Id, null);
		await userInputTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
		session.Status.ShouldBe(SessionStatusEnum.Running);
	}
}
