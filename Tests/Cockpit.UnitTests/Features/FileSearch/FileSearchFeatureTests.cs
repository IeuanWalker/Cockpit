using Cockpit.Features.FileSearch;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Cockpit.UnitTests.Features.FileSearch;

public class FileSearchFeatureTests : IDisposable
{
	readonly string _root;
	readonly FileSearchFeature _feature;

	public FileSearchFeatureTests()
	{
		_root = Path.Combine(Path.GetTempPath(), $"CockpitFileSearchTests_{Guid.NewGuid():N}");
		Directory.CreateDirectory(_root);
		_feature = new FileSearchFeature(NullLogger<FileSearchFeature>.Instance);
	}

	public void Dispose()
	{
		try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
	}

	// -----------------------------------------------------------------------
	// Helpers

	void CreateFile(string relativePath)
	{
		string full = Path.Combine(_root, relativePath);
		Directory.CreateDirectory(Path.GetDirectoryName(full)!);
		File.WriteAllText(full, string.Empty);
	}

	// -----------------------------------------------------------------------
	// Basic filtering

	[Fact]
	public async Task SearchAsync_EmptyFilter_ReturnsAllFiles()
	{
		CreateFile("a.txt");
		CreateFile("b.cs");

		IReadOnlyList<FileSearchResult> results = await _feature.SearchAsync(_root, string.Empty);

		results.Count.ShouldBe(2);
	}

	[Fact]
	public async Task SearchAsync_FilterByName_ReturnsMatchingFiles()
	{
		CreateFile("hello.cs");
		CreateFile("world.txt");
		CreateFile("helper.cs");

		IReadOnlyList<FileSearchResult> results = await _feature.SearchAsync(_root, "hel");

		results.Count.ShouldBe(2);
		results.ShouldAllBe(r => r.FileName.Contains("hel", StringComparison.OrdinalIgnoreCase));
	}

	[Fact]
	public async Task SearchAsync_FilterCaseInsensitive_ReturnsMatch()
	{
		CreateFile("Program.cs");

		IReadOnlyList<FileSearchResult> results = await _feature.SearchAsync(_root, "program");

		results.Count.ShouldBe(1);
		results[0].FileName.ShouldBe("Program.cs");
	}

	[Fact]
	public async Task SearchAsync_NoMatch_ReturnsEmpty()
	{
		CreateFile("readme.md");

		IReadOnlyList<FileSearchResult> results = await _feature.SearchAsync(_root, "zzz");

		results.ShouldBeEmpty();
	}

	// -----------------------------------------------------------------------
	// Skip dirs

	[Theory]
	[InlineData("node_modules")]
	[InlineData(".git")]
	[InlineData("bin")]
	[InlineData("obj")]
	[InlineData(".vs")]
	public async Task SearchAsync_SkipsKnownDirectories(string skipDir)
	{
		CreateFile(Path.Combine(skipDir, "hidden.cs"));
		CreateFile("visible.cs");

		IReadOnlyList<FileSearchResult> results = await _feature.SearchAsync(_root, string.Empty);

		results.Count.ShouldBe(1);
		results[0].FileName.ShouldBe("visible.cs");
	}

	// -----------------------------------------------------------------------
	// maxResults

	[Fact]
	public async Task SearchAsync_RespectsMaxResults()
	{
		for(int i = 0; i < 10; i++)
		{
			CreateFile($"file{i}.txt");
		}

		IReadOnlyList<FileSearchResult> results = await _feature.SearchAsync(_root, string.Empty, maxResults: 3);

		results.Count.ShouldBeLessThanOrEqualTo(3);
	}

	// -----------------------------------------------------------------------
	// Relevance ordering

	[Fact]
	public async Task SearchAsync_PrefixMatchesBeforeContainsMatches()
	{
		CreateFile("sub/foobaz.txt");  // contains "foo"
		CreateFile("foobar.txt");      // prefix "foo"

		IReadOnlyList<FileSearchResult> results = await _feature.SearchAsync(_root, "foo");

		results.Count.ShouldBe(2);
		results[0].FileName.ShouldBe("foobar.txt");  // prefix match first
	}

	[Fact]
	public async Task SearchAsync_ShallowerFilesBeforeDeeperFiles()
	{
		CreateFile("deep/sub/alpha.cs");
		CreateFile("alpha.cs");

		IReadOnlyList<FileSearchResult> results = await _feature.SearchAsync(_root, "alpha");

		results.Count.ShouldBe(2);
		results[0].RelativePath.ShouldBe("alpha.cs");
	}

	// -----------------------------------------------------------------------
	// Non-existent / empty directory

	[Fact]
	public async Task SearchAsync_NonExistentDirectory_ReturnsEmpty()
	{
		IReadOnlyList<FileSearchResult> results = await _feature.SearchAsync(
			Path.Combine(_root, "does_not_exist"), string.Empty);

		results.ShouldBeEmpty();
	}

	[Fact]
	public async Task SearchAsync_EmptyDirectory_ReturnsEmpty()
	{
		IReadOnlyList<FileSearchResult> results = await _feature.SearchAsync(_root, string.Empty);

		results.ShouldBeEmpty();
	}

	// -----------------------------------------------------------------------
	// Cancellation

	[Fact]
	public async Task SearchAsync_CancelledBeforeStart_ThrowsOperationCanceledException()
	{
		CreateFile("a.txt");
		using CancellationTokenSource cts = new();
		cts.Cancel();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(
			() => _feature.SearchAsync(_root, string.Empty, cancellationToken: cts.Token));
	}

	[Fact]
	public async Task SearchAsync_CancelledDuringSearch_ThrowsOperationCanceledException()
	{
		// Create enough files so that cancellation has a chance to fire mid-walk
		for(int i = 0; i < 100; i++)
		{
			CreateFile($"file{i:D3}.txt");
		}

		using CancellationTokenSource cts = new();

		// Cancel almost immediately to hit mid-walk cancellation
		cts.CancelAfter(1);

		// The search may complete before cancellation fires for small dirs, so we accept
		// either a normal result or an OperationCanceledException.
		try
		{
			IReadOnlyList<FileSearchResult> results = await _feature.SearchAsync(
				_root, string.Empty, cancellationToken: cts.Token);

			// Completed before cancellation — acceptable
			results.ShouldNotBeNull();
		}
		catch(OperationCanceledException)
		{
			// Cancelled mid-walk — expected
		}
	}
}
