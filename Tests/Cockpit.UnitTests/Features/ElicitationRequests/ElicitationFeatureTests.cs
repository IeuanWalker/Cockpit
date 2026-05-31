using System.Text.Json;
using Cockpit.Features.ElicitationRequests;
using Cockpit.Features.Permissions.Models;
using Cockpit.Features.Sessions;
using Cockpit.Features.Sessions.Models;
using Cockpit.Features.UserInputRequests;
using GitHub.Copilot;
using GitHub.Copilot.Rpc;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

#pragma warning disable GHCP001

namespace Cockpit.UnitTests.Features.ElicitationRequests;

public sealed class ElicitationFeatureTests
{
	static readonly ModelInfo testModel = new() { Id = "test", Name = "Test Model" };
	const string sessionId = "session-1";

	sealed class TestSessionStateProvider : ISessionStateProvider
	{
		readonly List<SessionModel> _sessions = [];

		public void AddSession(SessionModel session) => _sessions.Add(session);

		public IReadOnlyList<SessionModel> Sessions => _sessions;
		public SessionModel? CurrentSession => _sessions.FirstOrDefault();

		public void NotifyStateChanged() { }
	}

	static SessionModel CreateSession(string id = sessionId) => new()
	{
		Id = id,
		Title = "Test Session",
		CreatedAt = DateTime.UtcNow,
		LastActivity = DateTime.UtcNow,
		Model = testModel,
		Status = SessionStatusEnum.Idle,
		Context = new()
		{
			CurrentWorkingDirectory = "",
			WorkspacePath = null,
			GitRoot = null,
			Branch = null,
			Repository = null
		}
	};

	static (ElicitationFeature Feature, SessionModel Session, TestSessionStateProvider StateProvider) CreateFeature(string id = sessionId)
	{
		TestSessionStateProvider stateProvider = new();
		SessionModel session = CreateSession(id);
		stateProvider.AddSession(session);
		ElicitationFeature feature = new(stateProvider, NullLogger<ElicitationFeature>.Instance);
		return (feature, session, stateProvider);
	}

	static ElicitationContext BuildContext(string id = sessionId, string? message = "Please fill in your name", ElicitationSchema? schema = null) =>
		new()
		{
			SessionId = id,
			Message = message ?? string.Empty,
			RequestedSchema = schema,
			ElicitationSource = "test-source"
		};

	/// <summary>
	/// Starts <see cref="ElicitationFeature.HandleElicitationRequest"/> and captures the pending model
	/// via <see cref="ElicitationFeature.OnElicitationRequested"/> before returning.
	/// </summary>
	static async Task<(Task<ElicitationResult> HandleTask, ElicitationRequestModel CapturedModel)> StartHandleAsync(
		ElicitationFeature feature,
		ElicitationContext context)
	{
		TaskCompletionSource<ElicitationRequestModel> modelReady = new(TaskCreationOptions.RunContinuationsAsynchronously);

		feature.OnElicitationRequested += (_, model) => modelReady.TrySetResult(model);

		Task<ElicitationResult> handleTask = feature.HandleElicitationRequest(context);

		ElicitationRequestModel capturedModel = await modelReady.Task
			.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

		return (handleTask, capturedModel);
	}

	/// <summary>Builds an <see cref="ElicitationSchema"/> with properties described by a JSON object.</summary>
	static ElicitationSchema BuildSchema(string propertiesJson, IList<string>? required = null)
	{
		JsonDocument doc = JsonDocument.Parse(propertiesJson);
		Dictionary<string, object> props = [];
		foreach(JsonProperty prop in doc.RootElement.EnumerateObject())
		{
			props[prop.Name] = prop.Value.Clone();
		}

		return new ElicitationSchema { Properties = props, Required = required ?? [], Type = "object" };
	}

	// ── HandleElicitationRequest ──────────────────────────────────────────────

	[Fact]
	public async Task HandleElicitationRequest_SessionNotFound_ReturnsCancelled()
	{
		(ElicitationFeature feature, _, _) = CreateFeature();

		ElicitationResult result = await feature.HandleElicitationRequest(
			BuildContext("nonexistent-session"))
			.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

		result.Action.ShouldBe(UIElicitationResponseAction.Cancel);
	}

	[Fact]
	public async Task HandleElicitationRequest_SetsSessionStatusToNeedsElicitation()
	{
		(ElicitationFeature feature, SessionModel session, _) = CreateFeature();

		(Task<ElicitationResult> handleTask, ElicitationRequestModel model) = await StartHandleAsync(
			feature, BuildContext());

		session.Status.ShouldBe(SessionStatusEnum.NeedsElicitation);

		feature.ResolveElicitationRequest(model.Id, null);
		await handleTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
	}

	[Fact]
	public async Task HandleElicitationRequest_RestoredToPreviousStatusOnResolve()
	{
		(ElicitationFeature feature, SessionModel session, _) = CreateFeature();
		session.Status = SessionStatusEnum.Running;

		(Task<ElicitationResult> handleTask, ElicitationRequestModel model) = await StartHandleAsync(
			feature, BuildContext());

		feature.ResolveElicitationRequest(model.Id, null);
		await handleTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

		session.Status.ShouldBe(SessionStatusEnum.Running);
	}

	[Fact]
	public async Task HandleElicitationRequest_RestoredToIdleWhenNoStatusHistory()
	{
		(ElicitationFeature feature, SessionModel session, _) = CreateFeature();
		// session starts Idle, no history pushed yet

		(Task<ElicitationResult> handleTask, ElicitationRequestModel model) = await StartHandleAsync(
			feature, BuildContext());

		feature.ResolveElicitationRequest(model.Id, null);
		await handleTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

		session.Status.ShouldBe(SessionStatusEnum.Idle);
	}

	[Fact]
	public async Task HandleElicitationRequest_PendingRequestAddedAndRemovedFromSession()
	{
		(ElicitationFeature feature, SessionModel session, _) = CreateFeature();

		(Task<ElicitationResult> handleTask, ElicitationRequestModel model) = await StartHandleAsync(
			feature, BuildContext());

		session.PendingElicitationRequests.ContainsKey(model.Id).ShouldBeTrue();

		feature.ResolveElicitationRequest(model.Id, null);
		await handleTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

		session.PendingElicitationRequests.ContainsKey(model.Id).ShouldBeFalse();
	}

	[Fact]
	public async Task HandleElicitationRequest_OnElicitationRequestedFiredWithCorrectSessionAndMessage()
	{
		(ElicitationFeature feature, _, _) = CreateFeature();

		string? capturedSessionId = null;
		ElicitationRequestModel? capturedModel = null;

		feature.OnElicitationRequested += (sid, model) =>
		{
			capturedSessionId = sid;
			capturedModel = model;
		};

		(Task<ElicitationResult> handleTask, ElicitationRequestModel model) = await StartHandleAsync(
			feature, BuildContext(message: "What is your preference?"));

		capturedSessionId.ShouldBe(sessionId);
		capturedModel.ShouldNotBeNull();
		capturedModel.Message.ShouldBe("What is your preference?");
		capturedModel.SessionId.ShouldBe(sessionId);

		feature.ResolveElicitationRequest(model.Id, null);
		await handleTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
	}

	[Fact]
	public async Task HandleElicitationRequest_SecondConcurrentRequestDoesNotPushStatusHistoryAgain()
	{
		(ElicitationFeature feature, SessionModel session, _) = CreateFeature();
		session.Status = SessionStatusEnum.Running;

		(Task<ElicitationResult> task1, ElicitationRequestModel model1) = await StartHandleAsync(feature, BuildContext(message: "First"));
		(Task<ElicitationResult> task2, ElicitationRequestModel model2) = await StartHandleAsync(feature, BuildContext(message: "Second"));

		// Both pending — only one entry pushed to history (for the Running → NeedsElicitation transition)
		session.StatusHistory.Count.ShouldBe(1);
		session.Status.ShouldBe(SessionStatusEnum.NeedsElicitation);

		feature.ResolveElicitationRequest(model1.Id, null);
		await task1.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

		// One still pending — remains NeedsElicitation
		session.Status.ShouldBe(SessionStatusEnum.NeedsElicitation);

		feature.ResolveElicitationRequest(model2.Id, null);
		await task2.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

		// All resolved — restored to Running
		session.Status.ShouldBe(SessionStatusEnum.Running);
	}

	// ── Priority resolution ───────────────────────────────────────────────────

	[Fact]
	public async Task OnElicitationResolve_NeedsPermissionPrioritisedOverElicitation()
	{
		(ElicitationFeature feature, SessionModel session, _) = CreateFeature();

		// Simulate a pending permission request already on the session
		session.PendingPermissionRequests["perm-1"] = new PermissionRequestModel
		{
			SessionId = sessionId,
			FullCommand = "ls",
			Commands = ["ls"],
			RequestTitle = "Allow ls",
			Intention = string.Empty,
			CanApproveGlobally = true,
			CanApproveForSession = true,
			FullRequestJson = "{}"
		};

		(Task<ElicitationResult> handleTask, ElicitationRequestModel model) = await StartHandleAsync(
			feature, BuildContext());

		feature.ResolveElicitationRequest(model.Id, null);
		await handleTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

		// Permission is still pending — status must reflect that
		session.Status.ShouldBe(SessionStatusEnum.NeedsPermission);
	}

	[Fact]
	public async Task OnElicitationResolve_NeedsUserInputPrioritisedOverElicitation()
	{
		(ElicitationFeature feature, SessionModel session, _) = CreateFeature();

		// Simulate a pending user-input request already on the session
		session.PendingUserInputRequests["ui-1"] = new UserInputRequestModel
		{
			SessionId = sessionId,
			Question = "Continue?",
			Choices = [],
			AllowsTextInput = true,
			FullRequestJson = "{}"
		};

		(Task<ElicitationResult> handleTask, ElicitationRequestModel model) = await StartHandleAsync(
			feature, BuildContext());

		feature.ResolveElicitationRequest(model.Id, null);
		await handleTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

		// User-input is still pending — status must reflect that
		session.Status.ShouldBe(SessionStatusEnum.NeedsUserInput);
	}

	// ── ResolveElicitationRequest ─────────────────────────────────────────────

	[Fact]
	public void ResolveElicitationRequest_UnknownId_DoesNotThrow()
	{
		(ElicitationFeature feature, _, _) = CreateFeature();

		Should.NotThrow(() => feature.ResolveElicitationRequest("unknown-id", null));
	}

	[Fact]
	public async Task ResolveElicitationRequest_NullResult_ReturnsCancelAction()
	{
		(ElicitationFeature feature, _, _) = CreateFeature();

		(Task<ElicitationResult> handleTask, ElicitationRequestModel model) = await StartHandleAsync(
			feature, BuildContext());

		feature.ResolveElicitationRequest(model.Id, null);

		ElicitationResult result = await handleTask
			.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

		result.Action.ShouldBe(UIElicitationResponseAction.Cancel);
	}

	[Fact]
	public async Task ResolveElicitationRequest_WithResult_ReturnsProvidedAction()
	{
		(ElicitationFeature feature, _, _) = CreateFeature();

		(Task<ElicitationResult> handleTask, ElicitationRequestModel model) = await StartHandleAsync(
			feature, BuildContext());

		ElicitationResult response = new()
		{
			Action = UIElicitationResponseAction.Accept,
			Content = new Dictionary<string, object> { ["name"] = "Alice" }
		};

		feature.ResolveElicitationRequest(model.Id, response);

		ElicitationResult result = await handleTask
			.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

		result.Action.ShouldBe(UIElicitationResponseAction.Accept);
		result.Content!["name"].ShouldBe("Alice");
	}

	[Fact]
	public async Task ResolveElicitationRequest_AlreadyResolved_SecondCallIsNoOp()
	{
		(ElicitationFeature feature, _, _) = CreateFeature();

		(Task<ElicitationResult> handleTask, ElicitationRequestModel model) = await StartHandleAsync(
			feature, BuildContext());

		feature.ResolveElicitationRequest(model.Id, new ElicitationResult
		{
			Action = UIElicitationResponseAction.Accept,
			Content = new Dictionary<string, object>()
		});

		// Second call must not throw and must not change the first result
		Should.NotThrow(() => feature.ResolveElicitationRequest(model.Id, null));

		ElicitationResult result = await handleTask
			.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

		result.Action.ShouldBe(UIElicitationResponseAction.Accept);
	}

	// ── CancelPendingRequestsForSession ───────────────────────────────────────

	[Fact]
	public async Task CancelPendingRequestsForSession_CancelsAllPendingRequests()
	{
		(ElicitationFeature feature, _, _) = CreateFeature();

		(Task<ElicitationResult> taskA, _) = await StartHandleAsync(feature, BuildContext(message: "A"));
		(Task<ElicitationResult> taskB, _) = await StartHandleAsync(feature, BuildContext(message: "B"));

		feature.CancelPendingRequestsForSession(sessionId);

		ElicitationResult resultA = await taskA.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
		ElicitationResult resultB = await taskB.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

		resultA.Action.ShouldBe(UIElicitationResponseAction.Cancel);
		resultB.Action.ShouldBe(UIElicitationResponseAction.Cancel);
	}

	[Fact]
	public async Task CancelPendingRequestsForSession_LeavesOtherSessionUnaffected()
	{
		TestSessionStateProvider stateProvider = new();
		stateProvider.AddSession(CreateSession("session-A"));
		stateProvider.AddSession(CreateSession("session-B"));
		ElicitationFeature feature = new(stateProvider, NullLogger<ElicitationFeature>.Instance);

		(Task<ElicitationResult> taskA, _) = await StartHandleAsync(feature, BuildContext("session-A", "For A"));
		(Task<ElicitationResult> taskB, ElicitationRequestModel modelB) = await StartHandleAsync(feature, BuildContext("session-B", "For B"));

		feature.CancelPendingRequestsForSession("session-A");

		ElicitationResult resultA = await taskA.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
		resultA.Action.ShouldBe(UIElicitationResponseAction.Cancel);

		// session-B still pending — resolve it normally
		feature.ResolveElicitationRequest(modelB.Id, new ElicitationResult
		{
			Action = UIElicitationResponseAction.Accept,
			Content = new Dictionary<string, object>()
		});

		ElicitationResult resultB = await taskB.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
		resultB.Action.ShouldBe(UIElicitationResponseAction.Accept);
	}
}
