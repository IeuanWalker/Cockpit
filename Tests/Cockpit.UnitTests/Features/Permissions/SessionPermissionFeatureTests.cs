using Cockpit.Features.Permissions;
using Shouldly;

namespace Cockpit.UnitTests.Features.Permissions;

public class SessionPermissionFeatureTests
{
	[Fact]
	public void Add_SingleCommand_CommandIsAdded()
	{
		// Arrange
		SessionPermissionFeature feature = new();
		const string sessionId = "session1";
		const string command = "npm";

		// Act
		feature.Add(sessionId, command);

		// Assert
		feature.HasPermission(sessionId, command).ShouldBeTrue();
	}

	[Fact]
	public void Add_MultipleCommands_AllCommandsAreAdded()
	{
		// Arrange
		SessionPermissionFeature feature = new();
		const string sessionId = "session1";
		List<string> commands = ["npm", "yarn", "git"];

		// Act
		feature.Add(sessionId, commands);

		// Assert
		feature.HasPermissions(sessionId, commands).ShouldBeTrue();
	}

	[Fact]
	public void Add_DuplicateCommand_NoDuplicatesCreated()
	{
		// Arrange
		SessionPermissionFeature feature = new();
		const string sessionId = "session1";
		const string command = "npm";

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
		SessionPermissionFeature feature = new();

		// Act & Assert
		feature.HasPermission("nonexistent", "npm").ShouldBeFalse();
	}

	[Fact]
	public void HasPermissions_PartialMatch_ReturnsFalse()
	{
		// Arrange
		SessionPermissionFeature feature = new();
		const string sessionId = "session1";
		feature.Add(sessionId, "npm");

		// Act & Assert
		feature.HasPermissions(sessionId, ["npm", "yarn"]).ShouldBeFalse();
	}

	[Fact]
	public void Clear_RemovesAllSessionPermissions()
	{
		// Arrange
		SessionPermissionFeature feature = new();
		const string sessionId = "session1";
		feature.Add(sessionId, ["npm", "yarn", "git"]);

		// Act
		feature.Clear(sessionId);

		// Assert
		feature.HasPermission(sessionId, "npm").ShouldBeFalse();
		feature.GetAll(sessionId).ShouldBeEmpty();
	}

	[Fact]
	public void Clear_NonExistentSession_DoesNotThrow()
	{
		// Arrange
		SessionPermissionFeature feature = new();

		// Act & Assert
		Should.NotThrow(() => feature.Clear("nonexistent"));
	}

	[Fact]
	public void GetAll_NonExistentSession_ReturnsEmptyList()
	{
		// Arrange
		SessionPermissionFeature feature = new();

		// Act
		List<string> result = feature.GetAll("nonexistent");

		// Assert
		result.ShouldBeEmpty();
	}

	[Fact]
	public async Task ConcurrentAdd_SameSession_NoDuplicates()
	{
		// Arrange
		SessionPermissionFeature feature = new();
		const string sessionId = "session1";
		const string command = "npm";
		const int threadCount = 100;

		// Act
		Task[] tasks = new Task[threadCount];
		for(int i = 0; i < threadCount; i++)
		{
			tasks[i] = Task.Run(() => feature.Add(sessionId, command));
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
		SessionPermissionFeature feature = new();
		const string sessionId = "session1";
		const int commandCount = 100;

		// Act
		Task[] tasks = new Task[commandCount];
		for(int i = 0; i < commandCount; i++)
		{
			int index = i;
			tasks[i] = Task.Run(() => feature.Add(sessionId, $"command{index}"));
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
		SessionPermissionFeature feature = new();
		const int sessionCount = 10;
		const int commandsPerSession = 10;

		// Act
		List<Task> tasks = [];
		for(int s = 0; s < sessionCount; s++)
		{
			int sessionIndex = s;
			for(int c = 0; c < commandsPerSession; c++)
			{
				int commandIndex = c;
				tasks.Add(Task.Run(() =>
					feature.Add($"session{sessionIndex}", $"command{sessionIndex}_{commandIndex}")));
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
		SessionPermissionFeature feature = new();
		const string sessionId = "session1";
		const int iterations = 1000;
		List<string> commands = ["npm", "yarn", "git", "docker"];

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
			}));
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
			}));
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
		SessionPermissionFeature feature = new();
		const string sessionId = "session1";
		feature.Add(sessionId, ["npm", "yarn", "git"]);

		// Act - Multiple threads trying to clear simultaneously
		Task[] tasks = new Task[50];
		for(int i = 0; i < 50; i++)
		{
			tasks[i] = Task.Run(() => feature.Clear(sessionId));
		}

		// Should not throw
		await Should.NotThrowAsync(async () => await Task.WhenAll(tasks));

		// Assert
		feature.GetAll(sessionId).ShouldBeEmpty();
	}
}
