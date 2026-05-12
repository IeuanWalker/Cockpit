using Cockpit.Features.Permissions;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Cockpit.UnitTests.Features.Permissions;

public sealed class GlobalDenyFeatureTests : IDisposable
{
	readonly string _testDirectory;
	readonly string _testFilePath;

	public GlobalDenyFeatureTests()
	{
		_testDirectory = Path.Combine(Path.GetTempPath(), $"cockpit-deny-test-{Guid.NewGuid()}");
		Directory.CreateDirectory(_testDirectory);
		_testFilePath = Path.Combine(_testDirectory, "global-deny-commands.json");
	}

	public void Dispose()
	{
		if(Directory.Exists(_testDirectory))
		{
			Directory.Delete(_testDirectory, true);
		}
	}

	GlobalDenyFeature CreateFeature()
	{
		return new GlobalDenyFeature(NullLogger<GlobalDenyFeature>.Instance, _testFilePath);
	}

	[Fact]
	public void IsDenied_CommandNotAdded_ReturnsFalse()
	{
		// Arrange
		GlobalDenyFeature feature = CreateFeature();

		// Act & Assert
		feature.IsDenied("rm").ShouldBeFalse();
	}

	[Fact]
	public void IsDenied_CommandAdded_ReturnsTrue()
	{
		// Arrange
		GlobalDenyFeature feature = CreateFeature();

		// Act
		feature.Add("rm");

		// Assert
		feature.IsDenied("rm").ShouldBeTrue();
	}

	[Fact]
	public void IsDenied_CaseSensitive_ReturnsFalse()
	{
		// Arrange
		GlobalDenyFeature feature = CreateFeature();
		feature.Add("rm");

		// Act & Assert
		feature.IsDenied("RM").ShouldBeFalse();
		feature.IsDenied("Rm").ShouldBeFalse();
	}

	[Fact]
	public void AnyDenied_EmptyList_ReturnsFalse()
	{
		// Arrange
		GlobalDenyFeature feature = CreateFeature();
		feature.Add("rm");

		// Act & Assert
		feature.AnyDenied([]).ShouldBeFalse();
	}

	[Fact]
	public void AnyDenied_NoCommandsDenied_ReturnsFalse()
	{
		// Arrange
		GlobalDenyFeature feature = CreateFeature();

		// Act & Assert
		feature.AnyDenied(["npm", "git"]).ShouldBeFalse();
	}

	[Fact]
	public void AnyDenied_OneCommandDenied_ReturnsTrue()
	{
		// Arrange
		GlobalDenyFeature feature = CreateFeature();
		feature.Add("rm");

		// Act & Assert
		feature.AnyDenied(["npm", "rm", "git"]).ShouldBeTrue();
	}

	[Fact]
	public void AnyDenied_AllCommandsDenied_ReturnsTrue()
	{
		// Arrange
		GlobalDenyFeature feature = CreateFeature();
		feature.Add("rm");
		feature.Add("git push");

		// Act & Assert
		feature.AnyDenied(["rm", "git push"]).ShouldBeTrue();
	}

	[Fact]
	public void Add_DuplicateCommand_NoDuplicatesCreated()
	{
		// Arrange
		GlobalDenyFeature feature = CreateFeature();

		// Act
		feature.Add("rm");
		feature.Add("rm");
		feature.Add("rm");

		// Assert
		feature.GetAll().Count(c => c == "rm").ShouldBe(1);
	}

	[Fact]
	public void Remove_ExistingCommand_CommandIsRemoved()
	{
		// Arrange
		GlobalDenyFeature feature = CreateFeature();
		feature.Add("rm");

		// Act
		feature.Remove("rm");

		// Assert
		feature.IsDenied("rm").ShouldBeFalse();
	}

	[Fact]
	public void Remove_NonExistentCommand_DoesNotThrow()
	{
		// Arrange
		GlobalDenyFeature feature = CreateFeature();

		// Act & Assert
		Should.NotThrow(() => feature.Remove("nonexistent"));
	}

	[Fact]
	public void GetAll_ReturnsCommandsInAlphabeticalOrder()
	{
		// Arrange
		GlobalDenyFeature feature = CreateFeature();
		feature.Add("rm");
		feature.Add("apt");
		feature.Add("dd");

		// Act
		List<string> all = feature.GetAll();

		// Assert
		all.ShouldBe(["apt", "dd", "rm"], ignoreOrder: false);
	}

	[Fact]
	public void GetAll_EmptyFeature_ReturnsEmptyList()
	{
		// Arrange
		GlobalDenyFeature feature = CreateFeature();

		// Act & Assert
		feature.GetAll().ShouldBeEmpty();
	}

	[Fact]
	public void Add_RaisesOnDenyListChangedEvent()
	{
		// Arrange
		GlobalDenyFeature feature = CreateFeature();
		int eventCount = 0;
		feature.OnDenyListChanged += () => eventCount++;

		// Act
		feature.Add("rm");

		// Assert
		eventCount.ShouldBe(1);
	}

	[Fact]
	public void Add_DuplicateCommand_DoesNotRaiseEvent()
	{
		// Arrange
		GlobalDenyFeature feature = CreateFeature();
		int eventCount = 0;
		feature.OnDenyListChanged += () => eventCount++;

		// Act
		feature.Add("rm");
		feature.Add("rm");

		// Assert
		eventCount.ShouldBe(1);
	}

	[Fact]
	public void Remove_ExistingCommand_RaisesOnDenyListChangedEvent()
	{
		// Arrange
		GlobalDenyFeature feature = CreateFeature();
		feature.Add("rm");
		int eventCount = 0;
		feature.OnDenyListChanged += () => eventCount++;

		// Act
		feature.Remove("rm");

		// Assert
		eventCount.ShouldBe(1);
	}

	[Fact]
	public void Remove_NonExistentCommand_DoesNotRaiseEvent()
	{
		// Arrange
		GlobalDenyFeature feature = CreateFeature();
		int eventCount = 0;
		feature.OnDenyListChanged += () => eventCount++;

		// Act
		feature.Remove("nonexistent");

		// Assert
		eventCount.ShouldBe(0);
	}

	[Fact]
	public void Persistence_SavesAndLoadsFromFile()
	{
		// Arrange
		GlobalDenyFeature feature1 = CreateFeature();
		feature1.Add("rm");
		feature1.Add("apt");

		// Act - create a new instance from same file
		GlobalDenyFeature feature2 = CreateFeature();

		// Assert
		feature2.IsDenied("rm").ShouldBeTrue();
		feature2.IsDenied("apt").ShouldBeTrue();
		feature2.GetAll().Count.ShouldBe(2);
	}

	[Fact]
	public void Persistence_AfterRemove_RemovedCommandNotLoadedFromFile()
	{
		// Arrange
		GlobalDenyFeature feature1 = CreateFeature();
		feature1.Add("rm");
		feature1.Add("apt");
		feature1.Remove("rm");

		// Act
		GlobalDenyFeature feature2 = CreateFeature();

		// Assert
		feature2.IsDenied("rm").ShouldBeFalse();
		feature2.IsDenied("apt").ShouldBeTrue();
	}

	[Fact]
	public void NoFileExists_StartEmpty_DoesNotThrow()
	{
		// Arrange - use a path that definitely doesn't exist
		string nonExistentPath = Path.Combine(_testDirectory, "does-not-exist.json");

		// Act & Assert
		Should.NotThrow(() =>
		{
			GlobalDenyFeature feature = new(NullLogger<GlobalDenyFeature>.Instance, nonExistentPath);
			feature.GetAll().ShouldBeEmpty();
		});
	}

	[Fact]
	public void ConcurrentAccess_DoesNotThrow()
	{
		// Arrange
		GlobalDenyFeature feature = CreateFeature();

		// Act - Concurrent reads and writes
		List<Task> tasks = [];
		for(int i = 0; i < 50; i++)
		{
			int idx = i;
			tasks.Add(Task.Run(() => feature.Add($"cmd{idx}")));
			tasks.Add(Task.Run(() => feature.IsDenied($"cmd{idx}")));
			tasks.Add(Task.Run(() => feature.AnyDenied([$"cmd{idx}", $"cmd{idx + 1}"])));
			tasks.Add(Task.Run(() => feature.GetAll()));
		}

		// Assert
		Should.NotThrow(() => Task.WaitAll([.. tasks]));
	}
}
