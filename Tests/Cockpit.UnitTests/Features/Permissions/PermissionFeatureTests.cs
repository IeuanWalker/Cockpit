using Cockpit.Features.Permissions;
using Cockpit.Features.Permissions.Models;
using Cockpit.Features.Sessions;
using Cockpit.Features.Sessions.Models;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Cockpit.UnitTests.Features.Permissions;

public class PermissionFeatureTests
{
	static readonly ModelInfo testModel = new() { Id = "test", Name = "Test Model" };

	class TestSessionStateProvider : ISessionStateProvider
	{
		readonly List<SessionModel> _sessions = [];

		public void AddSession(SessionModel session) => _sessions.Add(session);

		public IReadOnlyList<SessionModel> Sessions => _sessions;

		public void NotifyStateChanged()
		{
			OnStateChanged?.Invoke();
		}

		public event Action? OnStateChanged;
	}

	[Fact]
	public async Task CheckPermissionAsync_YoloMode_AutoApproves()
	{
		// Arrange
		string testFile = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid()}.json");
		GlobalPermissionFeature globalFeature = new(NullLogger<GlobalPermissionFeature>.Instance, testFile);
		TestSessionStateProvider stateProvider = new();
		SessionPermissionFeature sessionFeature = new(stateProvider);
		string denyTestFile = Path.Combine(Path.GetTempPath(), $"test-deny-{Guid.NewGuid()}.json");
		GlobalDenyFeature denyFeature = new(NullLogger<GlobalDenyFeature>.Instance, denyTestFile);
		PermissionFeature feature = new(globalFeature, denyFeature, sessionFeature, stateProvider, NullLogger<PermissionFeature>.Instance);

		PermissionRequestModel request = new()
		{
			SessionId = "session1",
			FullCommand = "npm install",
			Commands = ["npm"],
			RequestTitle = "Allow npm",
			Intention = "test",
			CanApproveGlobally = true,
			CanApproveForSession = true,
			FullRequestJson = "{}"
		};

		// Act
		PermissionDecisionEnum result = await feature.CheckPermissionAsync(request, isYolo: true);

		// Assert
		result.ShouldBe(PermissionDecisionEnum.Once);

		globalFeature.Dispose();
	}

	[Fact]
	public async Task CheckPermissionAsync_GlobalPermissionExists_ReturnsGlobal()
	{
		// Arrange
		string testFile = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid()}.json");
		GlobalPermissionFeature globalFeature = new(NullLogger<GlobalPermissionFeature>.Instance, testFile);
		TestSessionStateProvider stateProvider = new();
		SessionPermissionFeature sessionFeature = new(stateProvider);
		string denyTestFile = Path.Combine(Path.GetTempPath(), $"test-deny-{Guid.NewGuid()}.json");
		GlobalDenyFeature denyFeature = new(NullLogger<GlobalDenyFeature>.Instance, denyTestFile);
		PermissionFeature feature = new(globalFeature, denyFeature, sessionFeature, stateProvider, NullLogger<PermissionFeature>.Instance);

		globalFeature.Add("npm");

		PermissionRequestModel request = new()
		{
			SessionId = "session1",
			FullCommand = "npm install",
			Commands = ["npm"],
			RequestTitle = "Allow npm",
			Intention = "test",
			CanApproveGlobally = true,
			CanApproveForSession = true,
			FullRequestJson = "{}"
		};

		// Act
		PermissionDecisionEnum result = await feature.CheckPermissionAsync(request);

		// Assert
		result.ShouldBe(PermissionDecisionEnum.Global);

		globalFeature.Dispose();
	}

	[Fact]
	public async Task CheckPermissionAsync_SessionPermissionExists_ReturnsSession()
	{
		// Arrange
		string testFile = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid()}.json");
		GlobalPermissionFeature globalFeature = new(NullLogger<GlobalPermissionFeature>.Instance, testFile);
		TestSessionStateProvider stateProvider = new();
		SessionPermissionFeature sessionFeature = new(stateProvider);
		string denyTestFile = Path.Combine(Path.GetTempPath(), $"test-deny-{Guid.NewGuid()}.json");
		GlobalDenyFeature denyFeature = new(NullLogger<GlobalDenyFeature>.Instance, denyTestFile);
		PermissionFeature feature = new(globalFeature, denyFeature, sessionFeature, stateProvider, NullLogger<PermissionFeature>.Instance);

		const string sessionId = "session1";
		stateProvider.AddSession(new SessionModel
		{
			Id = sessionId,
			Title = "Test Session",
			CreatedAt = DateTime.UtcNow,
			LastActivity = DateTime.UtcNow,
			Model = testModel,
			Context = new()
			{
				CurrentWorkingDirectory = "",
				WorkspacePath = null,
				GitRoot = null,
				Branch = null,
				Repository = null
			}
		});
		sessionFeature.Add(sessionId, "npm");

		PermissionRequestModel request = new()
		{
			SessionId = sessionId,
			FullCommand = "npm install",
			Commands = ["npm"],
			RequestTitle = "Allow npm",
			Intention = "test",
			CanApproveGlobally = true,
			CanApproveForSession = true,
			FullRequestJson = "{}"
		};

		// Act
		PermissionDecisionEnum result = await feature.CheckPermissionAsync(request);

		// Assert
		result.ShouldBe(PermissionDecisionEnum.Session);

		globalFeature.Dispose();
	}

	[Fact]
	public async Task CheckPermissionAsync_SessionHasPriority_OverGlobal()
	{
		// Arrange
		string testFile = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid()}.json");
		GlobalPermissionFeature globalFeature = new(NullLogger<GlobalPermissionFeature>.Instance, testFile);
		TestSessionStateProvider stateProvider = new();
		SessionPermissionFeature sessionFeature = new(stateProvider);
		string denyTestFile = Path.Combine(Path.GetTempPath(), $"test-deny-{Guid.NewGuid()}.json");
		GlobalDenyFeature denyFeature = new(NullLogger<GlobalDenyFeature>.Instance, denyTestFile);
		PermissionFeature feature = new(globalFeature, denyFeature, sessionFeature, stateProvider, NullLogger<PermissionFeature>.Instance);

		const string sessionId = "session1";
		stateProvider.AddSession(new SessionModel
		{
			Id = sessionId,
			Title = "Test Session",
			CreatedAt = DateTime.UtcNow,
			LastActivity = DateTime.UtcNow,
			Model = testModel,
			Context = new()
			{
				CurrentWorkingDirectory = "",
				WorkspacePath = null,
				GitRoot = null,
				Branch = null,
				Repository = null
			}
		});
		sessionFeature.Add(sessionId, "npm");
		globalFeature.Add("npm");

		PermissionRequestModel request = new()
		{
			SessionId = sessionId,
			FullCommand = "npm install",
			Commands = ["npm"],
			RequestTitle = "Allow npm",
			Intention = "test",
			CanApproveGlobally = true,
			CanApproveForSession = true,
			FullRequestJson = "{}"
		};

		// Act
		PermissionDecisionEnum result = await feature.CheckPermissionAsync(request);

		// Assert - Session takes priority
		result.ShouldBe(PermissionDecisionEnum.Session);

		globalFeature.Dispose();
	}

	[Fact]
	public async Task CheckPermissionAsync_PartiallyApprovedRequest_FiltersApprovedCommandsAndUpdatesTitle()
	{
		// Arrange
		string testFile = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid()}.json");
		GlobalPermissionFeature globalFeature = new(NullLogger<GlobalPermissionFeature>.Instance, testFile);
		TestSessionStateProvider stateProvider = new();
		SessionPermissionFeature sessionFeature = new(stateProvider);
		SessionModel session = new()
		{
			Id = "sessionId",
			Title = "Test Session",
			CreatedAt = DateTime.UtcNow,
			LastActivity = DateTime.UtcNow,
			Model = testModel,
			Context = new()
			{
				CurrentWorkingDirectory = "",
				WorkspacePath = null,
				GitRoot = null,
				Branch = null,
				Repository = null
			}
		};
		stateProvider.AddSession(session);
		string denyTestFile = Path.Combine(Path.GetTempPath(), $"test-deny-{Guid.NewGuid()}.json");
		GlobalDenyFeature denyFeature = new(NullLogger<GlobalDenyFeature>.Instance, denyTestFile);
		PermissionFeature feature = new(globalFeature, denyFeature, sessionFeature, stateProvider, NullLogger<PermissionFeature>.Instance);

		PermissionRequestModel approvedRequest = new()
		{
			SessionId = session.Id,
			FullCommand = "npm install",
			Commands = ["npm"],
			RequestTitle = "Allow npm",
			Intention = "test",
			CanApproveGlobally = true,
			CanApproveForSession = true,
			FullRequestJson = "{}"
		};

		_ = feature.CheckPermissionAsync(approvedRequest);
		feature.ResolvePermissionRequest(approvedRequest.Id, PermissionDecisionEnum.Global);

		PermissionRequestModel mixedRequest = new()
		{
			SessionId = session.Id,
			FullCommand = "npm install && git status",
			Commands = ["npm", "git"],
			RequestTitle = "Allow npm, git",
			Intention = "test",
			CanApproveGlobally = true,
			CanApproveForSession = true,
			FullRequestJson = "{}"
		};

		// Act
		_ = await feature.CheckPermissionAsync(mixedRequest);

		// Assert
		mixedRequest.Commands.ShouldBe(["git"]);
		mixedRequest.RequestTitle.ShouldBe("Allow git");

		globalFeature.Dispose();
	}

	[Fact]
	public void ResolvePermissionRequest_GlobalScope_SavesGlobalPermission()
	{
		// Arrange
		string testFile = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid()}.json");
		GlobalPermissionFeature globalFeature = new(NullLogger<GlobalPermissionFeature>.Instance, testFile);
		TestSessionStateProvider stateProvider = new();
		SessionPermissionFeature sessionFeature = new(stateProvider);
		SessionModel session = new()
		{
			Id = "sessionId",
			Title = "Test Session",
			CreatedAt = DateTime.UtcNow,
			LastActivity = DateTime.UtcNow,
			Model = testModel,
			Context = new()
			{
				CurrentWorkingDirectory = "",
				WorkspacePath = null,
				GitRoot = null,
				Branch = null,
				Repository = null
			}
		};
		stateProvider.AddSession(session);
		string denyTestFile = Path.Combine(Path.GetTempPath(), $"test-deny-{Guid.NewGuid()}.json");
		GlobalDenyFeature denyFeature = new(NullLogger<GlobalDenyFeature>.Instance, denyTestFile);
		PermissionFeature feature = new(globalFeature, denyFeature, sessionFeature, stateProvider, NullLogger<PermissionFeature>.Instance);

		PermissionRequestModel request = new()
		{
			SessionId = "session1",
			FullCommand = "npm install",
			Commands = ["npm"],
			RequestTitle = "Allow npm",
			Intention = "test",
			CanApproveGlobally = true,
			CanApproveForSession = true,
			FullRequestJson = "{}"
		};

		// Simulate a pending request
		_ = feature.CheckPermissionAsync(request);

		// Act
		feature.ResolvePermissionRequest(request.Id, PermissionDecisionEnum.Global);

		// Assert
		globalFeature.HasPermissions(["npm"]).ShouldBeTrue();
		sessionFeature.HasPermission("session1", "npm").ShouldBeFalse();

		globalFeature.Dispose();
	}

	[Fact]
	public void ResolvePermissionRequest_SessionScope_SavesSessionPermission()
	{
		// Arrange
		string testFile = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid()}.json");
		GlobalPermissionFeature globalFeature = new(NullLogger<GlobalPermissionFeature>.Instance, testFile);
		TestSessionStateProvider stateProvider = new();
		SessionPermissionFeature sessionFeature = new(stateProvider);
		SessionModel session = new()
		{
			Id = "session1",
			Title = "Test Session",
			CreatedAt = DateTime.UtcNow,
			LastActivity = DateTime.UtcNow,
			Model = testModel,
			Context = new()
			{
				CurrentWorkingDirectory = "",
				WorkspacePath = null,
				GitRoot = null,
				Branch = null,
				Repository = null
			}
		};
		stateProvider.AddSession(session);
		string denyTestFile = Path.Combine(Path.GetTempPath(), $"test-deny-{Guid.NewGuid()}.json");
		GlobalDenyFeature denyFeature = new(NullLogger<GlobalDenyFeature>.Instance, denyTestFile);
		PermissionFeature feature = new(globalFeature, denyFeature, sessionFeature, stateProvider, NullLogger<PermissionFeature>.Instance);

		PermissionRequestModel request = new()
		{
			SessionId = "session1",
			FullCommand = "npm install",
			Commands = ["npm"],
			RequestTitle = "Allow npm",
			Intention = "test",
			CanApproveGlobally = true,
			CanApproveForSession = true,
			FullRequestJson = "{}"
		};

		// Simulate a pending request
		_ = feature.CheckPermissionAsync(request);

		// Act
		feature.ResolvePermissionRequest(request.Id, PermissionDecisionEnum.Session);

		// Assert
		sessionFeature.HasPermission("session1", "npm").ShouldBeTrue();
		globalFeature.HasPermissions(["npm"]).ShouldBeFalse();

		globalFeature.Dispose();
	}

	[Fact]
	public void ResolvePermissionRequest_OnceScope_DoesNotSavePermission()
	{
		// Arrange
		string testFile = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid()}.json");
		GlobalPermissionFeature globalFeature = new(NullLogger<GlobalPermissionFeature>.Instance, testFile);
		TestSessionStateProvider stateProvider = new();
		SessionPermissionFeature sessionFeature = new(stateProvider);
		SessionModel session = new()
		{
			Id = "session1",
			Title = "Test Session",
			CreatedAt = DateTime.UtcNow,
			LastActivity = DateTime.UtcNow,
			Model = testModel,
			Context = new()
			{
				CurrentWorkingDirectory = "",
				WorkspacePath = null,
				GitRoot = null,
				Branch = null,
				Repository = null
			}
		};
		stateProvider.AddSession(session);
		string denyTestFile = Path.Combine(Path.GetTempPath(), $"test-deny-{Guid.NewGuid()}.json");
		GlobalDenyFeature denyFeature = new(NullLogger<GlobalDenyFeature>.Instance, denyTestFile);
		PermissionFeature feature = new(globalFeature, denyFeature, sessionFeature, stateProvider, NullLogger<PermissionFeature>.Instance);

		PermissionRequestModel request = new()
		{
			SessionId = "session1",
			FullCommand = "npm install",
			Commands = ["npm"],
			RequestTitle = "Allow npm",
			Intention = "test",
			CanApproveGlobally = true,
			CanApproveForSession = true,
			FullRequestJson = "{}"
		};

		// Simulate a pending request
		_ = feature.CheckPermissionAsync(request);

		// Act
		feature.ResolvePermissionRequest(request.Id, PermissionDecisionEnum.Once);

		// Assert
		sessionFeature.HasPermission("session1", "npm").ShouldBeFalse();
		globalFeature.HasPermissions(["npm"]).ShouldBeFalse();

		globalFeature.Dispose();
	}

	[Fact]
	public void ResolvePermissionRequest_Denied_DoesNotSavePermission()
	{
		// Arrange
		string testFile = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid()}.json");
		GlobalPermissionFeature globalFeature = new(NullLogger<GlobalPermissionFeature>.Instance, testFile);
		TestSessionStateProvider stateProvider = new();
		SessionPermissionFeature sessionFeature = new(stateProvider);
		SessionModel session = new()
		{
			Id = "session1",
			Title = "Test Session",
			CreatedAt = DateTime.UtcNow,
			LastActivity = DateTime.UtcNow,
			Model = testModel,
			Context = new()
			{
				CurrentWorkingDirectory = "",
				WorkspacePath = null,
				GitRoot = null,
				Branch = null,
				Repository = null
			}
		};
		stateProvider.AddSession(session);
		string denyTestFile = Path.Combine(Path.GetTempPath(), $"test-deny-{Guid.NewGuid()}.json");
		GlobalDenyFeature denyFeature = new(NullLogger<GlobalDenyFeature>.Instance, denyTestFile);
		PermissionFeature feature = new(globalFeature, denyFeature, sessionFeature, stateProvider, NullLogger<PermissionFeature>.Instance);

		PermissionRequestModel request = new()
		{
			SessionId = "session1",
			FullCommand = "npm install",
			Commands = ["npm"],
			RequestTitle = "Allow npm",
			Intention = "test",
			CanApproveGlobally = true,
			CanApproveForSession = true,
			FullRequestJson = "{}"
		};

		// Simulate a pending request
		_ = feature.CheckPermissionAsync(request);

		// Act
		feature.ResolvePermissionRequest(request.Id, PermissionDecisionEnum.Denied);

		// Assert
		sessionFeature.HasPermission("session1", "npm").ShouldBeFalse();
		globalFeature.HasPermissions(["npm"]).ShouldBeFalse();

		globalFeature.Dispose();
	}

	[Fact]
	public async Task AutoResolve_GlobalPermission_ResolvesMatchingPendingRequests()
	{
		// Arrange
		string testFile = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid()}.json");
		GlobalPermissionFeature globalFeature = new(NullLogger<GlobalPermissionFeature>.Instance, testFile);
		TestSessionStateProvider stateProvider = new();
		SessionPermissionFeature sessionFeature = new(stateProvider);
		SessionModel session1 = new()
		{
			Id = "session1",
			Title = "Test Session",
			CreatedAt = DateTime.UtcNow,
			LastActivity = DateTime.UtcNow,
			Model = testModel,
			Context = new()
			{
				CurrentWorkingDirectory = "",
				WorkspacePath = null,
				GitRoot = null,
				Branch = null,
				Repository = null
			}
		};
		SessionModel session2 = new()
		{
			Id = "session2",
			Title = "Test Session",
			CreatedAt = DateTime.UtcNow,
			LastActivity = DateTime.UtcNow,
			Model = testModel,
			Context = new()
			{
				CurrentWorkingDirectory = "",
				WorkspacePath = null,
				GitRoot = null,
				Branch = null,
				Repository = null
			}
		};
		stateProvider.AddSession(session1);
		stateProvider.AddSession(session2);
		string denyTestFile = Path.Combine(Path.GetTempPath(), $"test-deny-{Guid.NewGuid()}.json");
		GlobalDenyFeature denyFeature = new(NullLogger<GlobalDenyFeature>.Instance, denyTestFile);
		PermissionFeature feature = new(globalFeature, denyFeature, sessionFeature, stateProvider, NullLogger<PermissionFeature>.Instance);

		// Create two pending requests for the same command in different sessions
		PermissionRequestModel request1 = new()
		{
			SessionId = "session1",
			FullCommand = "npm install",
			Commands = ["npm"],
			RequestTitle = "Allow npm",
			Intention = "test",
			CanApproveGlobally = true,
			CanApproveForSession = true,
			FullRequestJson = "{}"
		};

		PermissionRequestModel request2 = new()
		{
			SessionId = "session2",
			FullCommand = "npm run build",
			Commands = ["npm"],
			RequestTitle = "Allow npm",
			Intention = "test",
			CanApproveGlobally = true,
			CanApproveForSession = true,
			FullRequestJson = "{}"
		};

		Task<PermissionDecisionEnum> task1 = feature.CheckPermissionAsync(request1);
		Task<PermissionDecisionEnum> task2 = feature.CheckPermissionAsync(request2);

		await Task.Delay(100, TestContext.Current.CancellationToken); // Let them start

		// Act - Approve first request globally
		feature.ResolvePermissionRequest(request1.Id, PermissionDecisionEnum.Global);

		// Assert - Second request should auto-resolve
		PermissionDecisionEnum result1 = await task1.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
		PermissionDecisionEnum result2 = await task2.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
		result1.ShouldBe(PermissionDecisionEnum.Global);
		result2.ShouldBe(PermissionDecisionEnum.Global);

		globalFeature.Dispose();
	}

	[Fact]
	public async Task AutoResolve_SessionPermission_OnlyResolvesMatchingSession()
	{
		// Arrange
		string testFile = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid()}.json");
		GlobalPermissionFeature globalFeature = new(NullLogger<GlobalPermissionFeature>.Instance, testFile);
		TestSessionStateProvider stateProvider = new();
		SessionPermissionFeature sessionFeature = new(stateProvider);
		SessionModel session1 = new()
		{
			Id = "session1",
			Title = "Test Session",
			CreatedAt = DateTime.UtcNow,
			LastActivity = DateTime.UtcNow,
			Model = testModel,
			Context = new()
			{
				CurrentWorkingDirectory = "",
				WorkspacePath = null,
				GitRoot = null,
				Branch = null,
				Repository = null
			}
		};
		SessionModel session2 = new()
		{
			Id = "session2",
			Title = "Test Session",
			CreatedAt = DateTime.UtcNow,
			LastActivity = DateTime.UtcNow,
			Model = testModel,
			Context = new()
			{
				CurrentWorkingDirectory = "",
				WorkspacePath = null,
				GitRoot = null,
				Branch = null,
				Repository = null
			}
		};
		stateProvider.AddSession(session1);
		stateProvider.AddSession(session2);
		string denyTestFile = Path.Combine(Path.GetTempPath(), $"test-deny-{Guid.NewGuid()}.json");
		GlobalDenyFeature denyFeature = new(NullLogger<GlobalDenyFeature>.Instance, denyTestFile);
		PermissionFeature feature = new(globalFeature, denyFeature, sessionFeature, stateProvider, NullLogger<PermissionFeature>.Instance);

		PermissionRequestModel request1 = new()
		{
			SessionId = "session1",
			FullCommand = "npm install",
			Commands = ["npm"],
			RequestTitle = "Allow npm",
			Intention = "test",
			CanApproveGlobally = true,
			CanApproveForSession = true,
			FullRequestJson = "{}"
		};

		PermissionRequestModel request2 = new()
		{
			SessionId = "session2",
			FullCommand = "npm run build",
			Commands = ["npm"],
			RequestTitle = "Allow npm",
			Intention = "test",
			CanApproveGlobally = true,
			CanApproveForSession = true,
			FullRequestJson = "{}"
		};

		Task<PermissionDecisionEnum> task1 = feature.CheckPermissionAsync(request1);
		Task<PermissionDecisionEnum> task2 = feature.CheckPermissionAsync(request2);

		await Task.Delay(100, TestContext.Current.CancellationToken);

		// Act - Approve first request for session only
		feature.ResolvePermissionRequest(request1.Id, PermissionDecisionEnum.Session);

		// Assert - Second request should still be pending (different session)
		PermissionDecisionEnum result1 = await task1.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
		result1.ShouldBe(PermissionDecisionEnum.Session);
		await Task.Delay(200, TestContext.Current.CancellationToken);
		task2.IsCompleted.ShouldBeFalse();

		// Clean up
		feature.ResolvePermissionRequest(request2.Id, PermissionDecisionEnum.Denied);

		globalFeature.Dispose();
	}

	[Fact]
	public async Task ConcurrentPermissionChecks_NoRaceConditions()
	{
		// Arrange
		string testFile = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid()}.json");
		GlobalPermissionFeature globalFeature = new(NullLogger<GlobalPermissionFeature>.Instance, testFile);
		TestSessionStateProvider stateProvider = new();
		SessionPermissionFeature sessionFeature = new(stateProvider);
		SessionModel session = new()
		{
			Id = "session1",
			Title = "Test Session",
			CreatedAt = DateTime.UtcNow,
			LastActivity = DateTime.UtcNow,
			Model = testModel,
			Context = new()
			{
				CurrentWorkingDirectory = "",
				WorkspacePath = null,
				GitRoot = null,
				Branch = null,
				Repository = null
			}
		};
		stateProvider.AddSession(session);
		string denyTestFile = Path.Combine(Path.GetTempPath(), $"test-deny-{Guid.NewGuid()}.json");
		GlobalDenyFeature denyFeature = new(NullLogger<GlobalDenyFeature>.Instance, denyTestFile);
		PermissionFeature feature = new(globalFeature, denyFeature, sessionFeature, stateProvider, NullLogger<PermissionFeature>.Instance);

		globalFeature.Add(["npm", "yarn", "git"]);

		// Act - Multiple concurrent permission checks
		List<Task<PermissionDecisionEnum>> tasks = [];
		for(int i = 0; i < 100; i++)
		{
			PermissionRequestModel request = new()
			{
				SessionId = "session1",
				FullCommand = $"npm install {i}",
				Commands = ["npm"],
				RequestTitle = "Allow npm",
				Intention = "test",
				CanApproveGlobally = true,
				CanApproveForSession = true,
				FullRequestJson = "{}"
			};
			tasks.Add(feature.CheckPermissionAsync(request));
		}

		// Should not throw
		PermissionDecisionEnum[] results = await Task.WhenAll(tasks);

		// Assert - All should return Global decision
		results.All(r => r == PermissionDecisionEnum.Global).ShouldBeTrue();

		globalFeature.Dispose();
	}

	[Fact]
	public async Task ResolvePermissionRequest_SessionScope_DoesNotResetStatusToIdle_WhenMultipleRequestsHaveSameCommands()
	{
		// Regression test: approving request A with Session scope must not cause status to revert to Idle.
		// Previously AutoResolveMatchingRequests included A itself, double-popping StatusHistory and landing on Idle.
		string testFile = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid()}.json");
		GlobalPermissionFeature globalFeature = new(NullLogger<GlobalPermissionFeature>.Instance, testFile);
		TestSessionStateProvider stateProvider = new();
		SessionPermissionFeature sessionFeature = new(stateProvider);
		string denyTestFile = Path.Combine(Path.GetTempPath(), $"test-deny-{Guid.NewGuid()}.json");
		GlobalDenyFeature denyFeature = new(NullLogger<GlobalDenyFeature>.Instance, denyTestFile);
		PermissionFeature feature = new(globalFeature, denyFeature, sessionFeature, stateProvider, NullLogger<PermissionFeature>.Instance);

		const string sessionId = "session1";
		SessionModel session = new()
		{
			Id = sessionId,
			Title = "Test Session",
			CreatedAt = DateTime.UtcNow,
			LastActivity = DateTime.UtcNow,
			Model = testModel,
			Status = SessionStatusEnum.Running,
			Context = new()
			{
				CurrentWorkingDirectory = "",
				WorkspacePath = null,
				GitRoot = null,
				Branch = null,
				Repository = null
			}
		};
		stateProvider.AddSession(session);

		// Two concurrent requests for the same command in the same session
		PermissionRequestModel requestA = new()
		{
			SessionId = sessionId,
			FullCommand = "npm install",
			Commands = ["npm"],
			RequestTitle = "Allow npm",
			Intention = "test",
			CanApproveGlobally = true,
			CanApproveForSession = true,
			FullRequestJson = "{}"
		};

		PermissionRequestModel requestB = new()
		{
			SessionId = sessionId,
			FullCommand = "npm run build",
			Commands = ["npm"],
			RequestTitle = "Allow npm",
			Intention = "test",
			CanApproveGlobally = true,
			CanApproveForSession = true,
			FullRequestJson = "{}"
		};

		Task<PermissionDecisionEnum> taskA = feature.CheckPermissionAsync(requestA);
		Task<PermissionDecisionEnum> taskB = feature.CheckPermissionAsync(requestB);

		await Task.Delay(100, TestContext.Current.CancellationToken);

		// Approve A with Session scope — B should auto-resolve, session should end up Running (not Idle)
		feature.ResolvePermissionRequest(requestA.Id, PermissionDecisionEnum.Session);

		PermissionDecisionEnum resultA = await taskA.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
		PermissionDecisionEnum resultB = await taskB.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

		resultA.ShouldBe(PermissionDecisionEnum.Session);
		resultB.ShouldBe(PermissionDecisionEnum.Session);
		session.Status.ShouldBe(SessionStatusEnum.Running);

		globalFeature.Dispose();
	}

	// Helper to access private field via reflection (for testing only)
	static List<PermissionRequestModel> GetPendingRequests(PermissionFeature feature)
	{
		System.Reflection.FieldInfo? field = typeof(PermissionFeature)
			.GetField("_pendingRequests", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

		if(field?.GetValue(feature) is System.Collections.Concurrent.ConcurrentDictionary<string, PermissionRequestModel> dict)
		{
			return [.. dict.Values];
		}

		return [];
	}
}


