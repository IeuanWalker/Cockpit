using Cockpit.Features.Terminal;
using Shouldly;

namespace Cockpit.UnitTests.Features.Terminal;

// TerminalSessionModel is internal; InternalsVisibleTo in Cockpit.csproj grants access.

public sealed class TerminalSessionModelTests
{
	static TerminalSessionModel CreateModel(string id = "test-session") =>
		new(id, new FakePtyConnection());

	// ── Initial state ──────────────────────────────────────────────────────

	[Fact]
	public void GetBuffer_Initially_ReturnsEmpty()
	{
		TerminalSessionModel model = CreateModel();
		model.GetBuffer().ShouldBeEmpty();
	}

	[Fact]
	public void Id_ReflectsConstructorArgument()
	{
		TerminalSessionModel model = CreateModel("my-id");
		model.Id.ShouldBe("my-id");
	}

	[Fact]
	public void DefaultDimensions_AreSet()
	{
		TerminalSessionModel model = CreateModel();
		model.Cols.ShouldBe(120);
		model.Rows.ShouldBe(30);
	}

	// ── BufferOutput ───────────────────────────────────────────────────────

	[Fact]
	public void BufferOutput_AppendsData()
	{
		TerminalSessionModel model = CreateModel();

		model.BufferOutput("hello ");
		model.BufferOutput("world");

		model.GetBuffer().ShouldBe("hello world");
	}

	[Fact]
	public void BufferOutput_EmptyString_DoesNotChangeBuffer()
	{
		TerminalSessionModel model = CreateModel();
		model.BufferOutput("data");
		model.BufferOutput(string.Empty);
		model.GetBuffer().ShouldBe("data");
	}

	[Fact]
	public void BufferOutput_TrimsToBoundary_WhenBufferExceedsMaxSize()
	{
		TerminalSessionModel model = CreateModel();

		// The max buffer size is 1 MB (1_048_576 chars).
		// Write 1 MB + 100 chars so trimming is required.
		string largeChunk = new('A', 1_048_576);
		string overflow = new('B', 100);

		model.BufferOutput(largeChunk);
		model.BufferOutput(overflow);

		string buffer = model.GetBuffer();
		buffer.Length.ShouldBe(1_048_576);
		// The tail should be the overflow characters that were appended last
		buffer[^100..].ShouldBe(overflow);
	}

	[Fact]
	public void BufferOutput_AtExactMaxSize_DoesNotTrim()
	{
		TerminalSessionModel model = CreateModel();

		string exactMaxChunk = new('X', 1_048_576);
		model.BufferOutput(exactMaxChunk);

		model.GetBuffer().Length.ShouldBe(1_048_576);
	}

	[Fact]
	public void BufferOutput_UnicodeCharacters_RoundTripsCorrectly()
	{
		TerminalSessionModel model = CreateModel();
		string unicode = "❯ λ 日本語 émoji 🚀";

		model.BufferOutput(unicode);

		model.GetBuffer().ShouldBe(unicode);
	}

	[Fact]
	public void BufferOutput_OneLessThanMaxSize_DoesNotTrim()
	{
		TerminalSessionModel model = CreateModel();
		string chunk = new('Z', 1_048_575); // one below 1 MB
		model.BufferOutput(chunk);

		model.GetBuffer().Length.ShouldBe(1_048_575);
	}

	// ── ClearBuffer ────────────────────────────────────────────────────────

	[Fact]
	public void ClearBuffer_EmptiesBuffer()
	{
		TerminalSessionModel model = CreateModel();
		model.BufferOutput("some output");

		model.ClearBuffer();

		model.GetBuffer().ShouldBeEmpty();
	}

	[Fact]
	public void ClearBuffer_OnEmptyBuffer_DoesNotThrow()
	{
		TerminalSessionModel model = CreateModel();
		Should.NotThrow(() => model.ClearBuffer());
	}

	[Fact]
	public void BufferOutput_AfterClear_AccumulatesNewData()
	{
		TerminalSessionModel model = CreateModel();
		model.BufferOutput("old data");
		model.ClearBuffer();

		model.BufferOutput("new data");

		model.GetBuffer().ShouldBe("new data");
	}

	// ── Thread safety ──────────────────────────────────────────────────────

	[Fact]
	public async Task BufferOutput_ConcurrentWrites_DoesNotCorrupt()
	{
		TerminalSessionModel model = CreateModel();
		const int threadCount = 20;
		const int writesPerThread = 50;

		// Each thread appends a single known character; total length must be exact
		Task[] tasks = [.. Enumerable.Range(0, threadCount)
			.Select(i => Task.Run(() =>
			{
				for(int j = 0; j < writesPerThread; j++)
				{
					model.BufferOutput("x");
				}
			}))];

		await Task.WhenAll(tasks);

		// No characters should be lost or corrupted
		model.GetBuffer().Length.ShouldBe(threadCount * writesPerThread);
	}

	[Fact]
	public async Task GetBuffer_ConcurrentReadsAndWrites_DoesNotThrow()
	{
		TerminalSessionModel model = CreateModel();

		Task writer = Task.Run(() =>
		{
			for(int i = 0; i < 200; i++)
			{
				model.BufferOutput("data");
			}
		}, TestContext.Current.CancellationToken);

		Task[] readers = [.. Enumerable.Range(0, 5)
			.Select(_ => Task.Run(() =>
			{
				for(int i = 0; i < 200; i++)
				{
					string ignored = model.GetBuffer();
				}
			}))];

		await Should.NotThrowAsync(() => Task.WhenAll([writer, .. readers]));
	}

	// ── ReadTask / CancellationTokenSource properties ──────────────────────

	[Fact]
	public void ReadTask_DefaultsToNull()
	{
		TerminalSessionModel model = CreateModel();
		model.ReadTask.ShouldBeNull();
	}

	[Fact]
	public void ReadTaskCancellation_DefaultsToNull()
	{
		TerminalSessionModel model = CreateModel();
		model.ReadTaskCancellation.ShouldBeNull();
	}
}
