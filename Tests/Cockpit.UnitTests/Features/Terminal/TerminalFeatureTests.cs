using Cockpit.Features.Terminal;
using Microsoft.Extensions.Logging.Abstractions;
using Porta.Pty;
using Shouldly;

namespace Cockpit.UnitTests.Features.Terminal;

/// <summary>
/// Unit tests for <see cref="TerminalFeature"/> that do not require a real PTY process.
/// Session creation uses the internal factory-injection constructor with a
/// <see cref="FakePtyConnection"/> factory, so no child process is spawned.
/// </summary>
public sealed class TerminalFeatureTests : IAsyncDisposable
{
	readonly TerminalFeature _feature = CreateFeature();

	public async ValueTask DisposeAsync() => await _feature.DisposeAsync();

	static TerminalFeature CreateFeature() =>
		new(NullLogger<TerminalFeature>.Instance,
			(_, _) => Task.FromResult<IPtyConnection>(new FakePtyConnection()));

	Task<bool> CreateSessionAsync(string sessionId, string workDir = ".") =>
		_feature.CreateSession(sessionId, workDir);

	void InjectOutput(string sessionId, string data) =>
		_feature.GetSession(sessionId)?.BufferOutput(data);

	void SimulateDataReceived(string sessionId, string data) =>
		_feature.RaiseDataReceived(sessionId, data);

	// ── CreateSession ─────────────────────────────────────────────────────────

	[Fact]
	public async Task CreateSession_NewSession_ReturnsTrue()
	{
		bool result = await CreateSessionAsync("new-session");
		result.ShouldBeTrue();
	}

	// ── GetBufferedOutput ───────────────────────────────────────────────────────

	[Fact]
	public void GetBufferedOutput_UnknownSession_ReturnsEmpty()
	{
		_feature.GetBufferedOutput("no-such-session").ShouldBeEmpty();
	}

	[Fact]
	public async Task GetBufferedOutput_KnownSession_ReturnsBufferedData()
	{
		await CreateSessionAsync("s1");
		InjectOutput("s1", "hello world");

		_feature.GetBufferedOutput("s1").ShouldBe("hello world");
	}

	// ── WriteAsync ──────────────────────────────────────────────────────────

	[Fact]
	public async Task WriteAsync_UnknownSession_ReturnsFalse()
	{
		bool result = await _feature.WriteAsync("no-such-session", "data", TestContext.Current.CancellationToken);
		result.ShouldBeFalse();
	}

	[Fact]
	public async Task WriteAsync_KnownSession_ReturnsTrue()
	{
		await CreateSessionAsync("s2");

		bool result = await _feature.WriteAsync("s2", "input", TestContext.Current.CancellationToken);

		result.ShouldBeTrue();
	}

	// ── SoftClear ────────────────────────────────────────────────────────────

	[Fact]
	public void SoftClear_UnknownSession_DoesNotThrow()
	{
		Should.NotThrow(() => _feature.SoftClear("no-such-session"));
	}

	[Fact]
	public async Task SoftClear_KnownSession_EmptiesBuffer()
	{
		await CreateSessionAsync("s3");
		InjectOutput("s3", "some output");

		_feature.SoftClear("s3");

		_feature.GetBufferedOutput("s3").ShouldBeEmpty();
	}

	// ── ResizePty ────────────────────────────────────────────────────────────

	[Fact]
	public void ResizePty_UnknownSession_DoesNotThrow()
	{
		Should.NotThrow(() => _feature.ResizePty("no-such-session", 80, 24));
	}

	[Fact]
	public async Task ResizePty_KnownSession_UpdatesSessionDimensions()
	{
		await CreateSessionAsync("s4");

		_feature.ResizePty("s4", 200, 50);

		_feature.GetSession("s4")!.Cols.ShouldBe(200);
		_feature.GetSession("s4")!.Rows.ShouldBe(50);
	}

	// ── CloseSessionAsync ─────────────────────────────────────────────────────

	[Fact]
	public async Task CloseSessionAsync_UnknownSession_DoesNotThrow()
	{
		await Should.NotThrowAsync(() => _feature.CloseSessionAsync("no-such-session"));
	}

	[Fact]
	public async Task CloseSessionAsync_KnownSession_RemovesSession()
	{
		await CreateSessionAsync("s5");

		await _feature.CloseSessionAsync("s5");

		_feature.GetBufferedOutput("s5").ShouldBeEmpty();
		_feature.GetSession("s5").ShouldBeNull();
	}

	// ── CreateSession duplicate guard ───────────────────────────────────────

	[Fact]
	public async Task CreateSession_DuplicateId_ReturnsFalse()
	{
		await CreateSessionAsync("s6");

		bool second = await CreateSessionAsync("s6");

		second.ShouldBeFalse();
	}

	// ── OnDataReceived event ──────────────────────────────────────────────────

	[Fact]
	public void OnDataReceived_EventSubscriber_ReceivesRaisedData()
	{
		string? receivedSessionId = null;
		string? receivedData = null;
		_feature.OnDataReceived += (sid, data) =>
		{
			receivedSessionId = sid;
			receivedData = data;
		};

		SimulateDataReceived("my-session", "ping");

		receivedSessionId.ShouldBe("my-session");
		receivedData.ShouldBe("ping");
	}

	[Fact]
	public async Task OnDataReceived_FiredThroughReadLoop_WhenPtyStreamHasData()
	{
		byte[] payload = System.Text.Encoding.UTF8.GetBytes("hello from pty");
		TerminalFeature featureWithData = new(
			NullLogger<TerminalFeature>.Instance,
			(_, _) => Task.FromResult<IPtyConnection>(new FakePtyConnection(payload)));

		string? capturedSessionId = null;
		string? capturedData = null;
		featureWithData.OnDataReceived += (sid, data) =>
		{
			capturedSessionId = sid;
			capturedData = data;
		};

		await featureWithData.CreateSession("stream-session", ".", TestContext.Current.CancellationToken);

		// Wait for the read loop to drain the pre-loaded stream
		Task? readTask = featureWithData.GetSession("stream-session")?.ReadTask;
		if(readTask is not null)
		{
			await readTask.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
		}

		await featureWithData.DisposeAsync();

		capturedSessionId.ShouldBe("stream-session");
		capturedData.ShouldBe("hello from pty");
	}

	// ── RestartSession ────────────────────────────────────────────────────────

	[Fact]
	public async Task RestartSession_ReplacesExistingSessionAndClearsBuffer()
	{
		await CreateSessionAsync("r1");
		InjectOutput("r1", "stale output");

		await _feature.RestartSession("r1", ".", TestContext.Current.CancellationToken);

		// A fresh session must exist with an empty buffer
		_feature.GetSession("r1").ShouldNotBeNull();
		_feature.GetBufferedOutput("r1").ShouldBeEmpty();
	}

	[Fact]
	public async Task RestartSession_WhenNoExistingSession_CreatesNewSession()
	{
		await _feature.RestartSession("r2", ".", TestContext.Current.CancellationToken);

		_feature.GetSession("r2").ShouldNotBeNull();
	}

	// ── DisposeAsync ──────────────────────────────────────────────────────────

	[Fact]
	public async Task DisposeAsync_WithActiveSessions_CompletesWithoutThrowing()
	{
		await CreateSessionAsync("d1");
		await CreateSessionAsync("d2");

		await Should.NotThrowAsync(() => _feature.DisposeAsync().AsTask());
	}
}
