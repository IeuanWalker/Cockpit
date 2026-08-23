using System.Text.Json;
using Cockpit.Features.Sessions;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Cockpit.UnitTests.Features.Sessions;

public sealed class PinnedItemsFeatureTests : IDisposable
{
	readonly string _directory;
	readonly string _filePath;

	public PinnedItemsFeatureTests()
	{
		_directory = Path.Combine(Path.GetTempPath(), $"CockpitPinnedItemsTests_{Guid.NewGuid():N}");
		_filePath = Path.Combine(_directory, "session-pins.json");
	}

	public void Dispose()
	{
		if(Directory.Exists(_directory))
		{
			Directory.Delete(_directory, recursive: true);
		}
	}

	PinnedItemsFeature CreateFeature() =>
		new(NullLogger<PinnedItemsFeature>.Instance, _filePath);

	[Fact]
	public async Task InitializeAsync_WhenFileDoesNotExist_StartsEmpty()
	{
		PinnedItemsFeature feature = CreateFeature();

		await feature.InitializeAsync();

		feature.IsSessionPinned("session-1").ShouldBeFalse();
		feature.IsProjectPinned("project-1").ShouldBeFalse();
	}

	[Fact]
	public async Task ToggleAsync_PersistsVersionedSessionAndProjectIds()
	{
		PinnedItemsFeature feature = CreateFeature();

		await feature.ToggleSessionAsync("session-1");
		await feature.ToggleProjectAsync("repo:OWNER/PROJECT");

		feature.IsSessionPinned("session-1").ShouldBeTrue();
		feature.IsProjectPinned("repo:OWNER/PROJECT").ShouldBeTrue();
		using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(
			_filePath,
			TestContext.Current.CancellationToken));
		document.RootElement.GetProperty("Version").GetInt32().ShouldBe(1);
		document.RootElement.GetProperty("SessionIds")[0].GetString().ShouldBe("session-1");
		document.RootElement.GetProperty("ProjectIds")[0].GetString().ShouldBe("repo:OWNER/PROJECT");
		File.Exists(_filePath + ".tmp").ShouldBeFalse();
	}

	[Fact]
	public async Task InitializeAsync_LoadsPersistedPinsAndIgnoresBlankIds()
	{
		Directory.CreateDirectory(_directory);
		await File.WriteAllTextAsync(
			_filePath,
			"""
			{
			  "Version": 1,
			  "SessionIds": ["session-1", "", "session-1"],
			  "ProjectIds": ["repo:OWNER/PROJECT", " "]
			}
			""",
			TestContext.Current.CancellationToken);
		PinnedItemsFeature feature = CreateFeature();

		await feature.InitializeAsync();

		feature.IsSessionPinned("session-1").ShouldBeTrue();
		feature.IsProjectPinned("repo:OWNER/PROJECT").ShouldBeTrue();
	}

	[Fact]
	public async Task ToggleAsync_WhenAlreadyPinned_UnpinsAndPersistsRemoval()
	{
		PinnedItemsFeature feature = CreateFeature();
		await feature.ToggleSessionAsync("session-1");
		await feature.ToggleProjectAsync("project-1");

		await feature.ToggleSessionAsync("session-1");
		await feature.ToggleProjectAsync("project-1");

		feature.IsSessionPinned("session-1").ShouldBeFalse();
		feature.IsProjectPinned("project-1").ShouldBeFalse();
		PinnedItemsFeature reloaded = CreateFeature();
		await reloaded.InitializeAsync();
		reloaded.IsSessionPinned("session-1").ShouldBeFalse();
		reloaded.IsProjectPinned("project-1").ShouldBeFalse();
	}

	[Fact]
	public async Task ReconcileAsync_RemovesOnlyStalePinsAndPersistsThem()
	{
		PinnedItemsFeature feature = CreateFeature();
		await feature.ToggleSessionAsync("valid-session");
		await feature.ToggleSessionAsync("stale-session");
		await feature.ToggleProjectAsync("valid-project");
		await feature.ToggleProjectAsync("stale-project");

		await feature.ReconcileAsync(
			new HashSet<string>(["valid-session"]),
			new HashSet<string>(["valid-project"]));

		feature.IsSessionPinned("valid-session").ShouldBeTrue();
		feature.IsSessionPinned("stale-session").ShouldBeFalse();
		feature.IsProjectPinned("valid-project").ShouldBeTrue();
		feature.IsProjectPinned("stale-project").ShouldBeFalse();

		PinnedItemsFeature reloaded = CreateFeature();
		await reloaded.InitializeAsync();
		reloaded.IsSessionPinned("valid-session").ShouldBeTrue();
		reloaded.IsSessionPinned("stale-session").ShouldBeFalse();
		reloaded.IsProjectPinned("valid-project").ShouldBeTrue();
		reloaded.IsProjectPinned("stale-project").ShouldBeFalse();
	}

	[Fact]
	public async Task ReconcileAsync_WhenNothingChanges_DoesNotNotifyOrRewriteFile()
	{
		PinnedItemsFeature feature = CreateFeature();
		await feature.ToggleSessionAsync("session-1");
		string before = await File.ReadAllTextAsync(_filePath, TestContext.Current.CancellationToken);
		int notifications = 0;
		feature.OnChanged += () => notifications++;

		await feature.ReconcileAsync(
			new HashSet<string>(["session-1"]),
			new HashSet<string>());

		notifications.ShouldBe(0);
		(await File.ReadAllTextAsync(_filePath, TestContext.Current.CancellationToken)).ShouldBe(before);
	}

	[Fact]
	public async Task InitializeAsync_WhenJsonIsMalformed_StartsEmpty()
	{
		Directory.CreateDirectory(_directory);
		await File.WriteAllTextAsync(
			_filePath,
			"not-json{{",
			TestContext.Current.CancellationToken);
		PinnedItemsFeature feature = CreateFeature();

		await Should.NotThrowAsync(feature.InitializeAsync);

		feature.IsSessionPinned("session-1").ShouldBeFalse();
		feature.IsProjectPinned("project-1").ShouldBeFalse();
	}

	[Fact]
	public async Task InitializeAsync_WhenVersionIsUnsupported_StartsEmpty()
	{
		Directory.CreateDirectory(_directory);
		await File.WriteAllTextAsync(
			_filePath,
			"""
			{ "Version": 99, "SessionIds": ["session-1"], "ProjectIds": ["project-1"] }
			""",
			TestContext.Current.CancellationToken);
		PinnedItemsFeature feature = CreateFeature();

		await feature.InitializeAsync();

		feature.IsSessionPinned("session-1").ShouldBeFalse();
		feature.IsProjectPinned("project-1").ShouldBeFalse();
	}

	[Fact]
	public async Task ConcurrentToggles_AreSerializedWithoutLosingPins()
	{
		PinnedItemsFeature feature = CreateFeature();
		Task[] toggles = [
			.. Enumerable.Range(0, 20).Select(index => feature.ToggleSessionAsync($"session-{index}")),
			.. Enumerable.Range(0, 20).Select(index => feature.ToggleProjectAsync($"project-{index}"))
		];

		await Task.WhenAll(toggles);

		foreach(int index in Enumerable.Range(0, 20))
		{
			feature.IsSessionPinned($"session-{index}").ShouldBeTrue();
			feature.IsProjectPinned($"project-{index}").ShouldBeTrue();
		}

		PinnedItemsFeature reloaded = CreateFeature();
		await reloaded.InitializeAsync();
		foreach(int index in Enumerable.Range(0, 20))
		{
			reloaded.IsSessionPinned($"session-{index}").ShouldBeTrue();
			reloaded.IsProjectPinned($"project-{index}").ShouldBeTrue();
		}
		File.Exists(_filePath + ".tmp").ShouldBeFalse();
	}

	[Fact]
	public async Task OnChanged_FiresAfterToggleAndChangedReconciliation()
	{
		PinnedItemsFeature feature = CreateFeature();
		await feature.InitializeAsync();
		int notifications = 0;
		feature.OnChanged += () => notifications++;

		await feature.ToggleSessionAsync("session-1");
		await feature.ReconcileAsync(new HashSet<string>(), new HashSet<string>());

		notifications.ShouldBe(2);
	}
}
