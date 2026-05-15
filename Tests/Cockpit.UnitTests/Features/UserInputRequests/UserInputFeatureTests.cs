using Cockpit.Features.Sessions;
using Cockpit.Features.Sessions.Models;
using Cockpit.Features.UserInputRequests;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Cockpit.UnitTests.Features.UserInputRequests;

public sealed class UserInputFeatureTests
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

	static (UserInputFeature Feature, SessionModel Session, TestSessionStateProvider StateProvider) CreateFeature(string sessionId = sessionId)
	{
		TestSessionStateProvider stateProvider = new();
		SessionModel session = CreateSession(sessionId);
		stateProvider.AddSession(session);
		UserInputFeature feature = new(stateProvider, NullLogger<UserInputFeature>.Instance);
		return (feature, session, stateProvider);
	}

	// ── Helpers ──────────────────────────────────────────────────────────────

	static UserInputRequest BuildRequest(string? question = "Continue?", string[]? choices = null, bool? allowFreeform = null) =>
		new()
		{
			Question = question ?? "Continue?",
			Choices = choices ?? [],
			AllowFreeform = allowFreeform
		};

	static UserInputInvocation BuildInvocation(string sessionId = sessionId) =>
		new() { SessionId = sessionId };

	/// <summary>Awaits <see cref="UserInputFeature.HandleUserInputRequest"/> and captures the pending model.</summary>
	static async Task<(Task<UserInputResponse> HandleTask, UserInputRequestModel CapturedModel)> StartHandleAsync(
		UserInputFeature feature,
		UserInputRequest sdkRequest,
		UserInputInvocation invocation)
	{
		UserInputRequestModel? captured = null;
		TaskCompletionSource<UserInputRequestModel> modelReady = new(TaskCreationOptions.RunContinuationsAsynchronously);

		feature.OnUserInputRequested += (_, model) =>
		{
			captured = model;
			modelReady.TrySetResult(model);
		};

		Task<UserInputResponse> handleTask = feature.HandleUserInputRequest(sdkRequest, invocation);

		UserInputRequestModel capturedModel = await modelReady.Task
			.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

		return (handleTask, capturedModel);
	}

	// ── HandleUserInputRequest ────────────────────────────────────────────────

	[Fact]
	public async Task HandleUserInputRequest_SessionNotFound_ReturnsEmptyResponse()
	{
		(UserInputFeature feature, _, _) = CreateFeature();

		UserInputResponse result = await feature.HandleUserInputRequest(
			BuildRequest(),
			BuildInvocation("nonexistent-session"))
			.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

		result.Answer.ShouldBe(string.Empty);
		result.WasFreeform.ShouldBe(false);
	}

	[Fact]
	public async Task HandleUserInputRequest_FreeTextResponse_ReturnsFreeformAnswer()
	{
		(UserInputFeature feature, _, _) = CreateFeature();

		(Task<UserInputResponse> handleTask, UserInputRequestModel model) = await StartHandleAsync(
			feature,
			BuildRequest(choices: ["Yes", "No"]),
			BuildInvocation());

		feature.ResolveUserInputRequest(model.Id, "custom answer");

		UserInputResponse result = await handleTask
			.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

		result.Answer.ShouldBe("custom answer");
		result.WasFreeform.ShouldBeTrue();
	}

	[Fact]
	public async Task HandleUserInputRequest_ChoiceResponse_IsNotFreeform()
	{
		(UserInputFeature feature, _, _) = CreateFeature();
		string[] choices = ["Option A", "Option B"];

		(Task<UserInputResponse> handleTask, UserInputRequestModel model) = await StartHandleAsync(
			feature,
			BuildRequest(choices: choices),
			BuildInvocation());

		feature.ResolveUserInputRequest(model.Id, "Option A");

		UserInputResponse result = await handleTask
			.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

		result.Answer.ShouldBe("Option A");
		result.WasFreeform.ShouldBeFalse();
	}

	[Fact]
	public async Task HandleUserInputRequest_CancelledResponse_ReturnsEmptyAnswer()
	{
		(UserInputFeature feature, _, _) = CreateFeature();

		(Task<UserInputResponse> handleTask, UserInputRequestModel model) = await StartHandleAsync(
			feature,
			BuildRequest(),
			BuildInvocation());

		feature.ResolveUserInputRequest(model.Id, null);

		UserInputResponse result = await handleTask
			.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

		result.Answer.ShouldBe(string.Empty);
	}

	[Fact]
	public async Task HandleUserInputRequest_AllowFreeformNull_DefaultsToTextInput()
	{
		(UserInputFeature feature, _, _) = CreateFeature();

		(Task<UserInputResponse> _, UserInputRequestModel model) = await StartHandleAsync(
			feature,
			BuildRequest(allowFreeform: null),
			BuildInvocation());

		model.AllowsTextInput.ShouldBeTrue();
		feature.ResolveUserInputRequest(model.Id, null);
	}

	[Fact]
	public async Task HandleUserInputRequest_AllowFreeformFalse_TextInputDisabled()
	{
		(UserInputFeature feature, _, _) = CreateFeature();

		(Task<UserInputResponse> _, UserInputRequestModel model) = await StartHandleAsync(
			feature,
			BuildRequest(allowFreeform: false),
			BuildInvocation());

		model.AllowsTextInput.ShouldBeFalse();
		feature.ResolveUserInputRequest(model.Id, null);
	}

	// ── ResolveUserInputRequest ───────────────────────────────────────────────

	[Fact]
	public void ResolveUserInputRequest_UnknownId_DoesNotThrow()
	{
		(UserInputFeature feature, _, _) = CreateFeature();

		Should.NotThrow(() => feature.ResolveUserInputRequest("unknown-request-id", "response"));
	}

	[Fact]
	public async Task ResolveUserInputRequest_AlreadyResolved_SecondCallIsNoOp()
	{
		(UserInputFeature feature, _, _) = CreateFeature();

		(Task<UserInputResponse> handleTask, UserInputRequestModel model) = await StartHandleAsync(
			feature,
			BuildRequest(),
			BuildInvocation());

		// First resolve
		feature.ResolveUserInputRequest(model.Id, "first");

		// Second resolve of same request — must not throw
		Should.NotThrow(() => feature.ResolveUserInputRequest(model.Id, "second"));

		UserInputResponse result = await handleTask
			.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

		// Only the first answer is applied
		result.Answer.ShouldBe("first");
	}

	// ── Events ───────────────────────────────────────────────────────────────

	[Fact]
	public async Task OnUserInputRequested_FiredWithCorrectSessionAndModel()
	{
		(UserInputFeature feature, _, _) = CreateFeature();

		string? capturedSessionId = null;
		UserInputRequestModel? capturedModel = null;

		feature.OnUserInputRequested += (sid, model) =>
		{
			capturedSessionId = sid;
			capturedModel = model;
		};

		(Task<UserInputResponse> handleTask, UserInputRequestModel model) = await StartHandleAsync(
			feature,
			BuildRequest(question: "Do you agree?"),
			BuildInvocation());

		capturedSessionId.ShouldBe(sessionId);
		capturedModel.ShouldNotBeNull();
		capturedModel.Question.ShouldBe("Do you agree?");

		feature.ResolveUserInputRequest(model.Id, null);
		await handleTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
	}

	[Fact]
	public async Task OnUserInputResolved_FiredAfterResolve()
	{
		(UserInputFeature feature, _, _) = CreateFeature();

		string? resolvedSessionId = null;
		string? resolvedRequestId = null;

		feature.OnUserInputResolved += (sid, rid) =>
		{
			resolvedSessionId = sid;
			resolvedRequestId = rid;
		};

		(Task<UserInputResponse> handleTask, UserInputRequestModel model) = await StartHandleAsync(
			feature,
			BuildRequest(),
			BuildInvocation());

		feature.ResolveUserInputRequest(model.Id, "done");

		await handleTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

		resolvedSessionId.ShouldBe(sessionId);
		resolvedRequestId.ShouldBe(model.Id);
	}

	// ── Session status ────────────────────────────────────────────────────────

	[Fact]
	public async Task SessionStatus_UpdatedToNeedsUserInputWhilePending()
	{
		(UserInputFeature feature, SessionModel session, _) = CreateFeature();

		(Task<UserInputResponse> handleTask, UserInputRequestModel model) = await StartHandleAsync(
			feature,
			BuildRequest(),
			BuildInvocation());

		session.Status.ShouldBe(SessionStatusEnum.NeedsUserInput);

		feature.ResolveUserInputRequest(model.Id, null);
		await handleTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
	}

	[Fact]
	public async Task SessionStatus_RestoredAfterResolve()
	{
		(UserInputFeature feature, SessionModel session, _) = CreateFeature();
		session.Status = SessionStatusEnum.Running;

		(Task<UserInputResponse> handleTask, UserInputRequestModel model) = await StartHandleAsync(
			feature,
			BuildRequest(),
			BuildInvocation());

		feature.ResolveUserInputRequest(model.Id, "yes");
		await handleTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

		// Status should be restored to the pre-request status
		session.Status.ShouldBe(SessionStatusEnum.Running);
	}

	[Fact]
	public async Task SessionStatus_PendingRequestAddedToSession()
	{
		(UserInputFeature feature, SessionModel session, _) = CreateFeature();

		(Task<UserInputResponse> handleTask, UserInputRequestModel model) = await StartHandleAsync(
			feature,
			BuildRequest(),
			BuildInvocation());

		session.PendingUserInputRequests.ContainsKey(model.Id).ShouldBeTrue();

		feature.ResolveUserInputRequest(model.Id, null);
		await handleTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

		session.PendingUserInputRequests.ContainsKey(model.Id).ShouldBeFalse();
	}

	// ── Cancellation ─────────────────────────────────────────────────────────

	[Fact]
	public async Task CancelPendingRequestsForSession_CancelsAllPendingRequests()
	{
		(UserInputFeature feature, _, _) = CreateFeature();

		(Task<UserInputResponse> taskA, UserInputRequestModel _) = await StartHandleAsync(
			feature, BuildRequest(question: "Question A"), BuildInvocation());
		(Task<UserInputResponse> taskB, UserInputRequestModel _) = await StartHandleAsync(
			feature, BuildRequest(question: "Question B"), BuildInvocation());

		feature.CancelPendingRequestsForSession(sessionId);

		UserInputResponse resultA = await taskA.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
		UserInputResponse resultB = await taskB.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

		resultA.Answer.ShouldBe(string.Empty);
		resultB.Answer.ShouldBe(string.Empty);
	}

	[Fact]
	public async Task CancelPendingRequestsForSession_LeavesOtherSessionUnaffected()
	{
		TestSessionStateProvider stateProvider = new();
		stateProvider.AddSession(CreateSession("session-A"));
		stateProvider.AddSession(CreateSession("session-B"));
		UserInputFeature feature = new(stateProvider, NullLogger<UserInputFeature>.Instance);

		(Task<UserInputResponse> taskA, _) = await StartHandleAsync(
			feature, BuildRequest(question: "For A"), BuildInvocation("session-A"));
		(Task<UserInputResponse> taskB, UserInputRequestModel modelB) = await StartHandleAsync(
			feature, BuildRequest(question: "For B"), BuildInvocation("session-B"));

		// Cancel only session-A
		feature.CancelPendingRequestsForSession("session-A");

		UserInputResponse resultA = await taskA.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
		resultA.Answer.ShouldBe(string.Empty);

		// session-B's request is still pending — resolve it normally
		feature.ResolveUserInputRequest(modelB.Id, "B answer");

		UserInputResponse resultB = await taskB.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
		resultB.Answer.ShouldBe("B answer");
	}

	// ── Concurrent requests ───────────────────────────────────────────────────

	[Fact]
	public async Task ConcurrentRequests_ResolvedIndependently()
	{
		(UserInputFeature feature, _, _) = CreateFeature();

		(Task<UserInputResponse> task1, UserInputRequestModel model1) = await StartHandleAsync(
			feature, BuildRequest(question: "First?"), BuildInvocation());
		(Task<UserInputResponse> task2, UserInputRequestModel model2) = await StartHandleAsync(
			feature, BuildRequest(question: "Second?"), BuildInvocation());

		// Resolve in reverse order to verify independence
		feature.ResolveUserInputRequest(model2.Id, "second answer");
		feature.ResolveUserInputRequest(model1.Id, "first answer");

		UserInputResponse result1 = await task1.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
		UserInputResponse result2 = await task2.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

		result1.Answer.ShouldBe("first answer");
		result2.Answer.ShouldBe("second answer");
	}

	[Fact]
	public async Task ConcurrentRequests_SecondRequestDoesNotPushStatusHistoryAgain()
	{
		(UserInputFeature feature, SessionModel session, _) = CreateFeature();
		session.Status = SessionStatusEnum.Running;

		(Task<UserInputResponse> task1, UserInputRequestModel model1) = await StartHandleAsync(
			feature, BuildRequest(question: "First?"), BuildInvocation());
		(Task<UserInputResponse> task2, UserInputRequestModel model2) = await StartHandleAsync(
			feature, BuildRequest(question: "Second?"), BuildInvocation());

		// Both pending — status history should only have one entry pushed
		session.StatusHistory.Count.ShouldBe(1);
		session.Status.ShouldBe(SessionStatusEnum.NeedsUserInput);

		feature.ResolveUserInputRequest(model1.Id, "one");
		await task1.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

		// Still one pending — should stay NeedsUserInput
		session.Status.ShouldBe(SessionStatusEnum.NeedsUserInput);

		feature.ResolveUserInputRequest(model2.Id, "two");
		await task2.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

		// All resolved — should restore Running
		session.Status.ShouldBe(SessionStatusEnum.Running);
	}

	// ── ToRequestModel ────────────────────────────────────────────────────────

	[Fact]
	public async Task RequestModel_FiltersEmptyChoices()
	{
		(UserInputFeature feature, _, _) = CreateFeature();

		(Task<UserInputResponse> handleTask, UserInputRequestModel model) = await StartHandleAsync(
			feature,
			BuildRequest(choices: ["Valid", "", "  ", "Also Valid"]),
			BuildInvocation());

		// Empty/whitespace-only entries are filtered out at the SDK level by the feature
		// (only genuinely empty strings are removed per the current implementation)
		model.Choices.ShouldNotContain(string.Empty);

		feature.ResolveUserInputRequest(model.Id, null);
		await handleTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
	}
}
