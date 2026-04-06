using Cockpit.Extensions;
using Cockpit.Features.Permissions;
using Cockpit.Features.Sessions;
using Cockpit.Features.Sessions.Models;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Cockpit.UnitTests.Features.Permissions;

public class SessionPermissionFeatureTests
{
	static readonly ModelInfo testModel = new() { Id = "test", Name = "Test Model" };

	class TestSessionStateProvider : ISessionStateProvider
	{
		readonly List<SessionModel> _sessions = [];

		public void AddSession(string sessionId, string? workspacePath = null)
		{
			if(_sessions.Any(s => s.Id == sessionId))
			{
				return;
			}

			_sessions.Add(new SessionModel
			{
				Id = sessionId,
				Title = $"Session {sessionId}",
				CreatedAt = DateTime.UtcNow,
				LastActivity = DateTime.UtcNow,
				Model = testModel,
				Context = new()
				{
					WorkspacePath = workspacePath,
					CurrentWorkingDirectory = string.Empty,
					GitRoot = null,
					Branch = null,
					Repository = null
				}
			});
		}

		public IReadOnlyList<SessionModel> Sessions => _sessions;
		public event Action? OnStateChanged;
		public void NotifyStateChanged() { }
	}

	static (SessionPermissionFeature Feature, TestSessionStateProvider StateProvider) CreateFeature(params string[] sessionIds)
	{
		TestSessionStateProvider stateProvider = new();
		foreach(string sessionId in sessionIds)
		{
			stateProvider.AddSession(sessionId);
		}

		return (new SessionPermissionFeature(stateProvider), stateProvider);
	}

	[Fact]
	public void Add_SingleCommand_CommandIsAdded()
	{
		// Arrange
		const string sessionId = "session1";
		const string command = "npm";
		(SessionPermissionFeature feature, _) = CreateFeature(sessionId);

		// Act
		feature.Add(sessionId, command);

		// Assert
		feature.HasPermission(sessionId, command).ShouldBeTrue();
	}

	[Fact]
	public void Add_MultipleCommands_AllCommandsAreAdded()
	{
		// Arrange
		const string sessionId = "session1";
		List<string> commands = ["npm", "yarn", "git"];
		(SessionPermissionFeature feature, _) = CreateFeature(sessionId);

		// Act
		feature.Add(sessionId, commands);

		// Assert
		feature.HasPermissions(sessionId, commands).ShouldBeTrue();
	}

	[Fact]
	public void Add_DuplicateCommand_NoDuplicatesCreated()
	{
		// Arrange
		const string sessionId = "session1";
		const string command = "npm";
		(SessionPermissionFeature feature, _) = CreateFeature(sessionId);

		// Act
		feature.Add(sessionId, command);
		feature.Add(sessionId, command);
		feature.Add(sessionId, command);

		// Assert
		List<string> allCommands = feature.GetAll(sessionId);
		allCommands.Count.ShouldBe(1);
		allCommands[0].ShouldBe(command);
	}

	[Fact]
	public void HasPermission_NonExistentSession_ReturnsFalse()
	{
		// Arrange
		(SessionPermissionFeature feature, _) = CreateFeature();

		// Act & Assert
		feature.HasPermission("nonexistent", "npm").ShouldBeFalse();
	}

	[Fact]
	public void HasPermissions_PartialMatch_ReturnsFalse()
	{
		// Arrange
		const string sessionId = "session1";
		(SessionPermissionFeature feature, _) = CreateFeature(sessionId);
		feature.Add(sessionId, "npm");

		// Act & Assert
		feature.HasPermissions(sessionId, ["npm", "yarn"]).ShouldBeFalse();
	}

	[Fact]
	public void Clear_RemovesAllSessionPermissions()
	{
		// Arrange
		const string sessionId = "session1";
		(SessionPermissionFeature feature, _) = CreateFeature(sessionId);
		feature.Add(sessionId, ["npm", "yarn", "git"]);

		// Act
		feature.Clear(sessionId);

		// Assert
		feature.HasPermission(sessionId, "npm").ShouldBeFalse();
		feature.GetAll(sessionId).ShouldBeEmpty();
	}

	[Fact]
	public void Remove_ExistingCommand_RemovesOnlyThatCommand()
	{
		// Arrange
		const string sessionId = "session1";
		(SessionPermissionFeature feature, _) = CreateFeature(sessionId);
		feature.Add(sessionId, ["npm", "yarn"]);

		// Act
		feature.Remove(sessionId, "npm");

		// Assert
		feature.HasPermission(sessionId, "npm").ShouldBeFalse();
		feature.HasPermission(sessionId, "yarn").ShouldBeTrue();
	}

	[Fact]
	public void Remove_NonExistentCommand_DoesNotThrow()
	{
		// Arrange
		const string sessionId = "session1";
		(SessionPermissionFeature feature, _) = CreateFeature(sessionId);
		feature.Add(sessionId, "npm");

		// Act & Assert
		Should.NotThrow(() => feature.Remove(sessionId, "yarn"));
	}

	[Fact]
	public void Clear_NonExistentSession_DoesNotThrow()
	{
		// Arrange
		(SessionPermissionFeature feature, _) = CreateFeature();

		// Act & Assert
		Should.NotThrow(() => feature.Clear("nonexistent"));
	}

	[Fact]
	public void GetAll_NonExistentSession_ReturnsEmptyList()
	{
		// Arrange
		(SessionPermissionFeature feature, _) = CreateFeature();

		// Act
		List<string> result = feature.GetAll("nonexistent");

		// Assert
		result.ShouldBeEmpty();
	}

	[Fact]
	public async Task ConcurrentAdd_SameSession_NoDuplicates()
	{
		// Arrange
		const string sessionId = "session1";
		const string command = "npm";
		const int threadCount = 100;
		(SessionPermissionFeature feature, _) = CreateFeature(sessionId);

		// Act
		Task[] tasks = new Task[threadCount];
		for(int i = 0; i < threadCount; i++)
		{
			tasks[i] = Task.Run(() => feature.Add(sessionId, command), TestContext.Current.CancellationToken);
		}
		await Task.WhenAll(tasks);

		// Assert
		List<string> allCommands = feature.GetAll(sessionId);
		allCommands.Count.ShouldBe(1);
		allCommands[0].ShouldBe(command);
	}

	[Fact]
	public async Task ConcurrentAdd_DifferentCommands_AllAdded()
	{
		// Arrange
		const string sessionId = "session1";
		const int commandCount = 100;
		(SessionPermissionFeature feature, _) = CreateFeature(sessionId);

		// Act
		Task[] tasks = new Task[commandCount];
		for(int i = 0; i < commandCount; i++)
		{
			int index = i;
			tasks[i] = Task.Run(() => feature.Add(sessionId, $"command{index}"), TestContext.Current.CancellationToken);
		}
		await Task.WhenAll(tasks);

		// Assert
		List<string> allCommands = feature.GetAll(sessionId);
		allCommands.Count.ShouldBe(commandCount);
		for(int i = 0; i < commandCount; i++)
		{
			allCommands.ShouldContain($"command{i}");
		}
	}

	[Fact]
	public async Task ConcurrentAdd_MultipleSessions_IsolatedCorrectly()
	{
		// Arrange
		const int sessionCount = 10;
		const int commandsPerSession = 10;
		(SessionPermissionFeature feature, TestSessionStateProvider stateProvider) = CreateFeature();
		for(int s = 0; s < sessionCount; s++)
		{
			stateProvider.AddSession($"session{s}");
		}

		// Act
		List<Task> tasks = [];
		for(int s = 0; s < sessionCount; s++)
		{
			int sessionIndex = s;
			for(int c = 0; c < commandsPerSession; c++)
			{
				int commandIndex = c;
				tasks.Add(Task.Run(() =>
					feature.Add($"session{sessionIndex}", $"command{sessionIndex}_{commandIndex}"), TestContext.Current.CancellationToken));
			}
		}
		await Task.WhenAll(tasks);

		// Assert
		for(int s = 0; s < sessionCount; s++)
		{
			List<string> sessionCommands = feature.GetAll($"session{s}");
			sessionCommands.Count.ShouldBe(commandsPerSession);
			for(int c = 0; c < commandsPerSession; c++)
			{
				sessionCommands.ShouldContain($"command{s}_{c}");
			}
		}
	}

	[Fact]
	public async Task ConcurrentReadWrite_NoRaceConditions()
	{
		// Arrange
		const string sessionId = "session1";
		const int iterations = 1000;
		List<string> commands = ["npm", "yarn", "git", "docker"];
		(SessionPermissionFeature feature, _) = CreateFeature(sessionId);

		// Act - Concurrent reads and writes
		List<Task> tasks = [];

		// Writers
		for(int i = 0; i < 10; i++)
		{
			tasks.Add(Task.Run(() =>
			{
				for(int j = 0; j < iterations; j++)
				{
					feature.Add(sessionId, commands[j % commands.Count]);
				}
			}, TestContext.Current.CancellationToken));
		}

		// Readers
		for(int i = 0; i < 10; i++)
		{
			tasks.Add(Task.Run(() =>
			{
				for(int j = 0; j < iterations; j++)
				{
					_ = feature.HasPermission(sessionId, commands[j % commands.Count]);
					_ = feature.GetAll(sessionId);
				}
			}, TestContext.Current.CancellationToken));
		}

		// Should not throw any exceptions
		await Should.NotThrowAsync(async () => await Task.WhenAll(tasks));

		// Assert - All commands should be present
		foreach(string command in commands)
		{
			feature.HasPermission(sessionId, command).ShouldBeTrue();
		}
	}

	[Fact]
	public async Task ConcurrentClear_NoExceptions()
	{
		// Arrange
		const string sessionId = "session1";
		(SessionPermissionFeature feature, _) = CreateFeature(sessionId);
		feature.Add(sessionId, ["npm", "yarn", "git"]);

		// Act - Multiple threads trying to clear simultaneously
		Task[] tasks = new Task[50];
		for(int i = 0; i < 50; i++)
		{
			tasks[i] = Task.Run(() => feature.Clear(sessionId), TestContext.Current.CancellationToken);
		}

		// Should not throw
		await Should.NotThrowAsync(async () => await Task.WhenAll(tasks));

		// Assert
		feature.GetAll(sessionId).ShouldBeEmpty();
	}

	[Fact]
	public void Add_WithWorkspacePath_PersistsCommandsToWorkspaceFile()
	{
		// Arrange
		const string sessionId = "session1";
		string workspacePath = Path.Combine(Path.GetTempPath(), $"cockpit-permissions-{Guid.NewGuid():N}");
		Directory.CreateDirectory(workspacePath);

		try
		{
			TestSessionStateProvider stateProvider = new();
			stateProvider.AddSession(sessionId, workspacePath);
			SessionPermissionFeature feature = new(stateProvider);

			// Act
			feature.Add(sessionId, "npm");

			// Assert
			string permissionsFilePath = Path.Combine(workspacePath, "Cockpit", "session-commands.json");
			File.Exists(permissionsFilePath).ShouldBeTrue();

			List<string>? storedCommands = File.ReadAllText(permissionsFilePath).DeserializeJson<List<string>>();
			storedCommands.ShouldNotBeNull();
			storedCommands.ShouldContain("npm");
		}
		finally
		{
			if(Directory.Exists(workspacePath))
			{
				Directory.Delete(workspacePath, recursive: true);
			}
		}
	}

	[Fact]
	public void TryRestoreSessionCommands_WhenFileExists_LoadsCommandsIntoContext()
	{
		// Arrange
		const string sessionId = "session1";
		string workspacePath = Path.Combine(Path.GetTempPath(), $"cockpit-permissions-{Guid.NewGuid():N}");
		string permissionsDirectory = Path.Combine(workspacePath, "Cockpit");
		string permissionsFilePath = Path.Combine(permissionsDirectory, "session-commands.json");
		Directory.CreateDirectory(permissionsDirectory);
#pragma warning disable CA1861 // Avoid constant arrays as arguments
		File.WriteAllText(permissionsFilePath, (new[] { "npm", "git" }).SerializeJson());
#pragma warning restore CA1861 // Avoid constant arrays as arguments

		SessionModel session = new()
		{
			Id = sessionId,
			Title = $"Session {sessionId}",
			CreatedAt = DateTime.UtcNow,
			LastActivity = DateTime.UtcNow,
			Model = testModel,
			Context = new()
			{
				WorkspacePath = workspacePath,
				CurrentWorkingDirectory = string.Empty,
				GitRoot = null,
				Branch = null,
				Repository = null
			}
		};

		try
		{
			// Act
			bool restored = SessionPermissionFeature.TryRestoreSessionCommands(session, NullLogger.Instance);

			// Assert
			restored.ShouldBeTrue();
			lock(session.Context.SessionPermissionCommandsLock)
			{
				session.Context.SessionPermissionCommands.ShouldContain("npm");
				session.Context.SessionPermissionCommands.ShouldContain("git");
			}
		}
		finally
		{
			if(Directory.Exists(workspacePath))
			{
				Directory.Delete(workspacePath, recursive: true);
			}
		}
	}
}
