using Cockpit.Features.Permissions;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Cockpit.UnitTests.Features.Permissions;

public sealed class GlobalPermissionFeatureTests : IDisposable
{
	readonly string _testDirectory;
	readonly string _testFilePath;

	public GlobalPermissionFeatureTests()
	{
		_testDirectory = Path.Combine(Path.GetTempPath(), $"cockpit-test-{Guid.NewGuid()}");
		Directory.CreateDirectory(_testDirectory);
		_testFilePath = Path.Combine(_testDirectory, "global-commands.json");
	}

	public void Dispose()
	{
		if(Directory.Exists(_testDirectory))
		{
			Directory.Delete(_testDirectory, true);
		}
	}

	GlobalPermissionFeature CreateFeature()
	{
		// Pass test-specific file path to avoid MAUI FileSystem dependency
		return new GlobalPermissionFeature(NullLogger<GlobalPermissionFeature>.Instance, _testFilePath);
	}

	[Fact]
	public void Add_SingleCommand_CommandIsAdded()
	{
		// Arrange
		using GlobalPermissionFeature feature = CreateFeature();
		const string command = "npm";

		// Act
		feature.Add(command);

		// Assert
		feature.HasPermissions([command]).ShouldBeTrue();
	}

	[Fact]
	public void Add_MultipleCommands_AllCommandsAreAdded()
	{
		// Arrange
		using GlobalPermissionFeature feature = CreateFeature();
		List<string> commands = ["npm", "yarn", "git"];

		// Act
		feature.Add(commands);

		// Assert
		feature.HasPermissions(commands).ShouldBeTrue();
	}

	[Fact]
	public void Add_DuplicateCommand_NoDuplicatesCreated()
	{
		// Arrange
		using GlobalPermissionFeature feature = CreateFeature();
		const string command = "npm";

		// Act
		feature.Add(command);
		feature.Add(command);
		feature.Add(command);

		// Assert
		List<string> allCommands = feature.GetAll();
		allCommands.Count(c => c == command).ShouldBe(1);
	}

	[Fact]
	public void HasPermissions_PartialMatch_ReturnsFalse()
	{
		// Arrange
		using GlobalPermissionFeature feature = CreateFeature();
		feature.Add("npm");

		// Act & Assert
		feature.HasPermissions(["npm", "yarn"]).ShouldBeFalse();
	}

	[Fact]
	public void Remove_ExistingCommand_CommandIsRemoved()
	{
		// Arrange
		using GlobalPermissionFeature feature = CreateFeature();
		const string command = "npm";
		feature.Add(command);

		// Act
		feature.Remove(command);

		// Assert
		feature.HasPermissions([command]).ShouldBeFalse();
	}

	[Fact]
	public void Remove_NonExistentCommand_DoesNotThrow()
	{
		// Arrange
		using GlobalPermissionFeature feature = CreateFeature();

		// Act & Assert
		Should.NotThrow(() => feature.Remove("nonexistent"));
	}

	[Fact]
	public void OnPermissionsChanged_Fired_WhenCommandAdded()
	{
		// Arrange
		using GlobalPermissionFeature feature = CreateFeature();
		bool eventFired = false;
		feature.OnPermissionsChanged += () => eventFired = true;

		// Act
		feature.Add("npm");

		// Assert
		eventFired.ShouldBeTrue();
	}

	[Fact]
	public void OnPermissionsChanged_Fired_WhenCommandRemoved()
	{
		// Arrange
		using GlobalPermissionFeature feature = CreateFeature();
		feature.Add("npm");
		bool eventFired = false;
		feature.OnPermissionsChanged += () => eventFired = true;

		// Act
		feature.Remove("npm");

		// Assert
		eventFired.ShouldBeTrue();
	}

	[Fact]
	public async Task ConcurrentAdd_SameCommand_NoDuplicates()
	{
		// Arrange
		using GlobalPermissionFeature feature = CreateFeature();
		const string command = "npm";
		const int threadCount = 100;

		// Act
		Task[] tasks = new Task[threadCount];
		for(int i = 0; i < threadCount; i++)
		{
			tasks[i] = Task.Run(() => feature.Add(command), TestContext.Current.CancellationToken);
		}
		await Task.WhenAll(tasks);

		// Assert
		List<string> allCommands = feature.GetAll();
		allCommands.Count(c => c == command).ShouldBe(1);
	}

	[Fact]
	public async Task ConcurrentAdd_DifferentCommands_AllAdded()
	{
		// Arrange
		using GlobalPermissionFeature feature = CreateFeature();
		const int commandCount = 100;

		// Act
		Task[] tasks = new Task[commandCount];
		for(int i = 0; i < commandCount; i++)
		{
			int index = i;
			tasks[i] = Task.Run(() => feature.Add($"command{index}"), TestContext.Current.CancellationToken);
		}
		await Task.WhenAll(tasks);

		// Assert
		List<string> allCommands = feature.GetAll();
		allCommands.Count.ShouldBe(commandCount);
		for(int i = 0; i < commandCount; i++)
		{
			allCommands.ShouldContain($"command{i}");
		}
	}

	[Fact]
	public async Task ConcurrentReadWrite_NoRaceConditions()
	{
		// Arrange
		using GlobalPermissionFeature feature = CreateFeature();
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
					feature.Add(commands[j % commands.Count]);
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
					_ = feature.HasPermissions([commands[j % commands.Count]]);
					_ = feature.GetAll();
				}
			}, TestContext.Current.CancellationToken));
		}

		// Should not throw any exceptions
		await Should.NotThrowAsync(async () => await Task.WhenAll(tasks));

		// Assert - All commands should be present
		foreach(string command in commands)
		{
			feature.HasPermissions([command]).ShouldBeTrue();
		}
	}

	[Fact]
	public async Task ConcurrentAddRemove_NoExceptions()
	{
		// Arrange
		using GlobalPermissionFeature feature = CreateFeature();
		const int iterations = 500;
		List<string> commands = ["npm", "yarn", "git", "docker"];

		// Act - Concurrent adds and removes
		List<Task> tasks = [];

		// Adders
		for(int i = 0; i < 5; i++)
		{
			tasks.Add(Task.Run(() =>
			{
				for(int j = 0; j < iterations; j++)
				{
					feature.Add(commands[j % commands.Count]);
				}
			}, TestContext.Current.CancellationToken));
		}

		// Removers
		for(int i = 0; i < 5; i++)
		{
			tasks.Add(Task.Run(() =>
			{
				for(int j = 0; j < iterations; j++)
				{
					feature.Remove(commands[j % commands.Count]);
				}
			}, TestContext.Current.CancellationToken));
		}

		// Should not throw
		await Should.NotThrowAsync(async () => await Task.WhenAll(tasks));
	}

	[Fact]
	public async Task ConcurrentBulkAdd_NoRaceConditions()
	{
		// Arrange
		using GlobalPermissionFeature feature = CreateFeature();
		const int threadCount = 50;
		List<string> commands = ["npm", "yarn", "git", "docker"];

		// Act
		Task[] tasks = new Task[threadCount];
		for(int i = 0; i < threadCount; i++)
		{
			tasks[i] = Task.Run(() => feature.Add(commands), TestContext.Current.CancellationToken);
		}
		await Task.WhenAll(tasks);

		// Assert - Should have exactly the commands added (no duplicates)
		List<string> allCommands = feature.GetAll();
		allCommands.Count.ShouldBe(commands.Count);
		foreach(string command in commands)
		{
			allCommands.ShouldContain(command);
		}
	}

	[Fact]
	public void Persistence_SavesAndLoadsFromFile()
	{
		// Arrange - add commands to first instance (writes to file)
		using GlobalPermissionFeature feature1 = CreateFeature();
		feature1.Add("npm");
		feature1.Add("yarn");

		// Act - create new instance from the same file
		using GlobalPermissionFeature feature2 = CreateFeature();

		// Assert - commands loaded from disk
		feature2.HasPermission("npm").ShouldBeTrue();
		feature2.HasPermission("yarn").ShouldBeTrue();
		feature2.GetAll().Count.ShouldBe(2);
	}

	[Fact]
	public void Persistence_AfterRemove_RemovedCommandNotLoadedFromFile()
	{
		// Arrange
		using GlobalPermissionFeature feature1 = CreateFeature();
		feature1.Add("npm");
		feature1.Add("yarn");
		feature1.Remove("npm");

		// Act
		using GlobalPermissionFeature feature2 = CreateFeature();

		// Assert
		feature2.HasPermission("npm").ShouldBeFalse();
		feature2.HasPermission("yarn").ShouldBeTrue();
	}

	[Fact]
	public void Persistence_NoFileExists_StartsEmpty()
	{
		// Arrange - use a path that definitely doesn't exist
		string nonExistentPath = Path.Combine(_testDirectory, "does-not-exist.json");

		// Act & Assert - starts empty without throwing
		Should.NotThrow(() =>
		{
			using GlobalPermissionFeature feature = new(NullLogger<GlobalPermissionFeature>.Instance, nonExistentPath);
			feature.GetAll().ShouldBeEmpty();
		});
	}

	[Fact]
	public void GetAll_ReturnsCommandsInAlphabeticalOrder()
	{
		// Arrange
		using GlobalPermissionFeature feature = CreateFeature();
		feature.Add("yarn");
		feature.Add("npm");
		feature.Add("docker");

		// Act
		List<string> all = feature.GetAll();

		// Assert
		all.ShouldBe(["docker", "npm", "yarn"], ignoreOrder: false);
	}

	[Fact]
	public void OnPermissionsChanged_NotFired_WhenDuplicateAdded()
	{
		// Arrange
		using GlobalPermissionFeature feature = CreateFeature();
		feature.Add("npm");
		int eventCount = 0;
		feature.OnPermissionsChanged += () => eventCount++;

		// Act - add duplicate
		feature.Add("npm");

		// Assert
		eventCount.ShouldBe(0);
	}

	[Fact]
	public void HasPermission_SingleCommand_ReturnsCorrectly()
	{
		// Arrange
		using GlobalPermissionFeature feature = CreateFeature();
		feature.Add("npm");

		// Act & Assert
		feature.HasPermission("npm").ShouldBeTrue();
		feature.HasPermission("yarn").ShouldBeFalse();
	}

	[Fact]
	public void Remove_NonExistentCommand_DoesNotFireEvent()
	{
		// Arrange
		using GlobalPermissionFeature feature = CreateFeature();
		int eventCount = 0;
		feature.OnPermissionsChanged += () => eventCount++;

		// Act
		feature.Remove("nonexistent");

		// Assert
		eventCount.ShouldBe(0);
	}

	[Fact]
	public void Dispose_ReleasesResources()
	{
		// Arrange
		GlobalPermissionFeature feature = CreateFeature();
		feature.Add("npm");

		// Act & Assert - Should not throw
		Should.NotThrow(() => feature.Dispose());
		Should.NotThrow(() => feature.Dispose()); // Double dispose should be safe
	}
}
