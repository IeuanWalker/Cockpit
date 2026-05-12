using Cockpit.Features.Sdk;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Cockpit.UnitTests.Features.Sdk;

public sealed class CopilotClientFeatureTests
{
	static CopilotClientFeature CreateFeature() => new(NullLogger<CopilotClientFeature>.Instance);

	// ── State ─────────────────────────────────────────────────────────────────

	[Fact]
	public async Task State_BeforeClientCreated_IsDisconnected()
	{
		await using CopilotClientFeature feature = CreateFeature();

		feature.State.ShouldBe(ConnectionState.Disconnected);
	}

	[Fact]
	public async Task State_AfterDispose_IsDisconnected()
	{
		CopilotClientFeature feature = CreateFeature();
		await feature.DisposeAsync();

		// State must be safely readable after disposal (no ObjectDisposedException).
		feature.State.ShouldBe(ConnectionState.Disconnected);
	}

	// ── StopAsync no-op ───────────────────────────────────────────────────────

	[Fact]
	public async Task StopAsync_WhenClientNotCreated_DoesNotThrow()
	{
		await using CopilotClientFeature feature = CreateFeature();

		await Should.NotThrowAsync(() => feature.StopAsync());
	}

	[Fact]
	public async Task StopAsync_WhenClientNotCreated_DoesNotFireOnConnectionStateChanged()
	{
		await using CopilotClientFeature feature = CreateFeature();
		bool eventFired = false;
		feature.OnConnectionStateChanged += _ => eventFired = true;

		await feature.StopAsync();

		eventFired.ShouldBeFalse();
	}

	// ── Disposal idempotency ──────────────────────────────────────────────────

	[Fact]
	public async Task DisposeAsync_CalledTwice_DoesNotThrow()
	{
		CopilotClientFeature feature = CreateFeature();

		await feature.DisposeAsync();

		await Should.NotThrowAsync(() => feature.DisposeAsync().AsTask());
	}

	// ── GetClientAsync disposal guard ─────────────────────────────────────────

	[Fact]
	public async Task GetClientAsync_AfterDispose_ThrowsObjectDisposedException()
	{
		CopilotClientFeature feature = CreateFeature();
		await feature.DisposeAsync();

		await Should.ThrowAsync<ObjectDisposedException>(
		() => feature.GetClientAsync(TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task GetClientAsync_WithAlreadyCancelledToken_ThrowsOperationCanceledException()
	{
		// Cancellation is forwarded to _clientLock.WaitAsync, so this throws before
		// any client construction begins — no live SDK required.
		await using CopilotClientFeature feature = CreateFeature();

		using CancellationTokenSource cts = new();
		await cts.CancelAsync();

		await Should.ThrowAsync<OperationCanceledException>(
		() => feature.GetClientAsync(cts.Token));
	}

	// ── RestartAsync disposal guard ───────────────────────────────────────────

	[Fact]
	public async Task RestartAsync_AfterDispose_ThrowsObjectDisposedException()
	{
		// RestartAsync calls StopAsync (no-op) then GetClientAsync, which sees _disposed
		// and throws ObjectDisposedException.
		CopilotClientFeature feature = CreateFeature();
		await feature.DisposeAsync();

		await Should.ThrowAsync<ObjectDisposedException>(
		() => feature.RestartAsync(TestContext.Current.CancellationToken));
	}

	// ── ICopilotPingService contract ──────────────────────────────────────────

	[Fact]
	public async Task PingAsync_WhenDisposed_ReturnsNull()
	{
		// PingAsync catches all exceptions (including ObjectDisposedException from the
		// disposed client lock) and returns null — this tests the ICopilotPingService
		// contract is safe to call without first checking disposal state.
		CopilotClientFeature feature = CreateFeature();
		ICopilotPingService pingService = feature;
		await feature.DisposeAsync();

		PingResponse? result = await pingService.PingAsync(TestContext.Current.CancellationToken);

		result.ShouldBeNull();
	}

	[Fact]
	public async Task PingAsync_WhenCancelled_ReturnsNull()
	{
		// PingAsync must swallow OperationCanceledException the same way it handles
		// any other exception, returning null rather than propagating.
		await using CopilotClientFeature feature = CreateFeature();
		ICopilotPingService pingService = feature;

		using CancellationTokenSource cts = new();
		await cts.CancelAsync();

		PingResponse? result = await pingService.PingAsync(cts.Token);

		result.ShouldBeNull();
	}

	// ── Post-dispose safety ───────────────────────────────────────────────────

	[Fact]
	public async Task StopAsync_AfterDispose_DoesNotThrow()
	{
		// After disposal _client is null, so StopAsync is a no-op rather than
		// throwing from a disposed SemaphoreSlim.
		CopilotClientFeature feature = CreateFeature();
		await feature.DisposeAsync();

		await Should.NotThrowAsync(() => feature.StopAsync());
	}

	[Fact]
	public async Task DisposeAsync_ConcurrentCalls_DoNotThrow()
	{
		CopilotClientFeature feature = CreateFeature();

		Task[] calls =
		[
		feature.DisposeAsync().AsTask(),
feature.DisposeAsync().AsTask(),
feature.DisposeAsync().AsTask()
		];

		await Should.NotThrowAsync(() => Task.WhenAll(calls));
	}

	// ── Disposal cleans up lock ──────────────────────────────────────────────

	[Fact]
	public async Task DisposeAsync_DisposesClientLock()
	{
		CopilotClientFeature feature = CreateFeature();

		await feature.DisposeAsync();

		// After disposal, StopAsync should be a no-op (early return on _disposed check)
		// rather than trying to use the disposed SemaphoreSlim
		await Should.NotThrowAsync(() => feature.StopAsync());
	}

	// ── State readable after concurrent dispose + stop ────────────────────────

	[Fact]
	public async Task State_DuringConcurrentDisposeAndStop_AlwaysReturnsDisconnected()
	{
		CopilotClientFeature feature = CreateFeature();
		CancellationToken ct = TestContext.Current.CancellationToken;

		Task[] calls =
		[
			feature.DisposeAsync().AsTask(),
			Task.Run(() => feature.StopAsync(), ct),
			Task.Run(() => feature.StopAsync(), ct),
		];

		await Should.NotThrowAsync(() => Task.WhenAll(calls));
		feature.State.ShouldBe(ConnectionState.Disconnected);
	}
}