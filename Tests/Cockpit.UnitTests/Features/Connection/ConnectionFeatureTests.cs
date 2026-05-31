using Cockpit.Features.Connection;
using Cockpit.Features.Sdk;
using GitHub.Copilot;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Cockpit.UnitTests.Features.Connection;

public class ConnectionFeatureTests
{
	/// <summary>
	/// Minimal in-process fake that delegates ping calls to the provided handler,
	/// eliminating the need for a mocking framework.
	/// </summary>
	sealed class FakePingService : ICopilotPingService
	{
		readonly Func<CancellationToken, Task<PingResponse?>> _handler;

		public FakePingService(Func<CancellationToken, Task<PingResponse?>> handler)
		{
			_handler = handler;
		}

		public Task<PingResponse?> PingAsync(CancellationToken cancellationToken = default)
			=> _handler(cancellationToken);

		public event Action<ConnectionState>? OnConnectionStateChanged;
	}

	static ConnectionFeature CreateFeature(Func<CancellationToken, Task<PingResponse?>> handler)
		=> new(new FakePingService(handler), NullLogger<ConnectionFeature>.Instance);

	static ConnectionFeature CreateConnectedFeature()
		=> CreateFeature(_ => Task.FromResult<PingResponse?>(new PingResponse()));

	static ConnectionFeature CreateDisconnectedFeature()
		=> CreateFeature(_ => Task.FromResult<PingResponse?>(null));

	static ConnectionFeature CreateErrorFeature()
		=> CreateFeature(_ => throw new InvalidOperationException("simulated ping failure"));

	// ── Status transitions ────────────────────────────────────────────────────

	[Fact]
	public void Status_BeforePing_IsUnknown()
	{
		ConnectionFeature feature = CreateConnectedFeature();

		feature.Status.ShouldBe(ConnectionStatusEnum.Unknown);
	}

	[Fact]
	public async Task Ping_OnSuccessfulResponse_SetsStatusConnected()
	{
		ConnectionFeature feature = CreateConnectedFeature();

		await feature.Ping(TestContext.Current.CancellationToken);

		feature.Status.ShouldBe(ConnectionStatusEnum.Connected);
	}

	[Fact]
	public async Task Ping_OnNullResponse_SetsStatusDisconnected()
	{
		ConnectionFeature feature = CreateDisconnectedFeature();

		await feature.Ping(TestContext.Current.CancellationToken);

		feature.Status.ShouldBe(ConnectionStatusEnum.Disconnected);
	}

	[Fact]
	public async Task Ping_OnException_SetsStatusError()
	{
		ConnectionFeature feature = CreateErrorFeature();

		await feature.Ping(TestContext.Current.CancellationToken);

		feature.Status.ShouldBe(ConnectionStatusEnum.Error);
	}

	// ── LastChecked ───────────────────────────────────────────────────────────

	[Fact]
	public void LastChecked_BeforePing_IsNull()
	{
		ConnectionFeature feature = CreateConnectedFeature();

		feature.LastChecked.ShouldBeNull();
	}

	[Fact]
	public async Task Ping_SetsLastChecked()
	{
		ConnectionFeature feature = CreateConnectedFeature();
		DateTime before = DateTime.UtcNow;

		await feature.Ping(TestContext.Current.CancellationToken);

		feature.LastChecked.ShouldNotBeNull();
		feature.LastChecked!.Value.ShouldBeGreaterThanOrEqualTo(before);
	}

	// ── OnStatusChanged event ─────────────────────────────────────────────────

	[Fact]
	public async Task Ping_OnSuccess_FiresStatusChangedEvent()
	{
		ConnectionFeature feature = CreateConnectedFeature();
		bool eventFired = false;
		feature.OnStatusChanged += () => eventFired = true;

		await feature.Ping(TestContext.Current.CancellationToken);

		eventFired.ShouldBeTrue();
	}

	[Fact]
	public async Task Ping_OnError_FiresStatusChangedEvent()
	{
		ConnectionFeature feature = CreateErrorFeature();
		bool eventFired = false;
		feature.OnStatusChanged += () => eventFired = true;

		await feature.Ping(TestContext.Current.CancellationToken);

		eventFired.ShouldBeTrue();
	}

	// ── History ───────────────────────────────────────────────────────────────

	[Fact]
	public async Task Ping_AddsRecordToHistory()
	{
		ConnectionFeature feature = CreateConnectedFeature();

		await feature.Ping(TestContext.Current.CancellationToken);

		feature.History.Count.ShouldBe(1);
		feature.History[0].Status.ShouldBe(ConnectionStatusEnum.Connected);
	}

	[Fact]
	public async Task Ping_OnError_AddsErrorRecordToHistory()
	{
		ConnectionFeature feature = CreateErrorFeature();

		await feature.Ping(TestContext.Current.CancellationToken);

		feature.History.Count.ShouldBe(1);
		feature.History[0].Status.ShouldBe(ConnectionStatusEnum.Error);
		feature.History[0].ResponseJson.ShouldContain("simulated ping failure");
	}

	[Fact]
	public async Task History_IsBoundedToMaxHistorySize()
	{
		ConnectionFeature feature = CreateConnectedFeature();

		for(int i = 0; i <= ConnectionFeature.MaxHistorySize; i++)
		{
			await feature.Ping(TestContext.Current.CancellationToken);
		}

		feature.History.Count.ShouldBe(ConnectionFeature.MaxHistorySize);
	}

	[Fact]
	public async Task History_OldestEntryEvictedWhenFull()
	{
		int callIndex = 0;
		ConnectionFeature feature = CreateFeature(_ =>
		{
			Interlocked.Increment(ref callIndex);
			// First ping returns null (Disconnected), rest return connected
			return callIndex == 1
				? Task.FromResult<PingResponse?>(null)
				: Task.FromResult<PingResponse?>(new PingResponse());
		});

		// Fill to capacity, causing the first (Disconnected) entry to be evicted
		for(int i = 0; i <= ConnectionFeature.MaxHistorySize; i++)
		{
			await feature.Ping(TestContext.Current.CancellationToken);
		}

		feature.History.Count.ShouldBe(ConnectionFeature.MaxHistorySize);
		feature.History.ShouldAllBe(r => r.Status == ConnectionStatusEnum.Connected);
	}

	// ── Initialize idempotency ────────────────────────────────────────────────

	[Fact]
	public async Task Initialize_CalledTwice_OnlyPingsOnce()
	{
		int pingCount = 0;
		ConnectionFeature feature = CreateFeature(_ =>
		{
			Interlocked.Increment(ref pingCount);
			return Task.FromResult<PingResponse?>(null);
		});

		await feature.Initialize(TestContext.Current.CancellationToken);
		await feature.Initialize(TestContext.Current.CancellationToken);

		pingCount.ShouldBe(1);
	}

	[Fact]
	public async Task Initialize_AfterCancelledInit_AllowsSubsequentInitialization()
	{
		int pingCount = 0;
		ConnectionFeature feature = CreateFeature(_ =>
		{
			int count = Interlocked.Increment(ref pingCount);
			// First call is cancelled; subsequent calls succeed
			return count == 1
				? Task.FromCanceled<PingResponse?>(new CancellationToken(canceled: true))
				: Task.FromResult<PingResponse?>(new PingResponse());
		});

		using CancellationTokenSource cancelledCts = new();
		await cancelledCts.CancelAsync();

		// First call: cancelled ping causes Initialize to reset state and return early
		await feature.Initialize(cancelledCts.Token);

		// Second call on the same feature: state was reset, so initialization runs again
		await feature.Initialize(TestContext.Current.CancellationToken);

		pingCount.ShouldBe(2);
		feature.Status.ShouldBe(ConnectionStatusEnum.Connected);
	}

	// ── Non-reentrant Ping ────────────────────────────────────────────────────

	[Fact]
	public async Task Ping_ConcurrentCalls_SecondCallIsSkipped()
	{
		TaskCompletionSource<PingResponse?> gate = new();
		int pingCount = 0;

		ConnectionFeature feature = CreateFeature(_ =>
		{
			Interlocked.Increment(ref pingCount);
			return gate.Task;
		});

		// Start first ping (it will block on the gate)
		Task first = feature.Ping(TestContext.Current.CancellationToken);

		// Second ping should be skipped since the first is still in progress
		await feature.Ping(TestContext.Current.CancellationToken);

		// Release the gate and complete the first ping
		gate.SetResult(new PingResponse());
		await first;

		pingCount.ShouldBe(1);
	}

	// ── GetResponseJson ───────────────────────────────────────────────────────

	[Fact]
	public void GetResponseJson_BeforePing_ContainsUnreachableStatus()
	{
		ConnectionFeature feature = CreateConnectedFeature();

		string json = feature.GetResponseJson();

		json.ShouldNotBeNullOrEmpty();
		json.ShouldContain("unreachable");
	}

	[Fact]
	public async Task GetResponseJson_AfterSuccessfulPing_IsNonEmpty()
	{
		ConnectionFeature feature = CreateConnectedFeature();

		await feature.Ping(TestContext.Current.CancellationToken);

		feature.GetResponseJson().ShouldNotBeNullOrEmpty();
	}

	// ── CancellationToken ─────────────────────────────────────────────────────

	[Fact]
	public async Task Ping_WhenCancelled_ReturnsWithoutChangingStatus()
	{
		// The fake returns an already-cancelled Task regardless of which token is passed in,
		// so we can still honour TestContext.Current.CancellationToken on the Ping call itself.
		ConnectionFeature feature = CreateFeature(
			_ => Task.FromCanceled<PingResponse?>(new CancellationToken(canceled: true)));

		await feature.Ping(TestContext.Current.CancellationToken);

		// Cancelled ping must not overwrite status to Disconnected/Error
		feature.Status.ShouldBe(ConnectionStatusEnum.Unknown);
	}

	// ── LastResponse ──────────────────────────────────────────────────────────

	[Fact]
	public async Task Ping_OnSuccessfulResponse_SetsLastResponseToNonNull()
	{
		ConnectionFeature feature = CreateConnectedFeature();

		await feature.Ping(TestContext.Current.CancellationToken);

		feature.LastResponse.ShouldNotBeNull();
	}

	[Fact]
	public async Task Ping_OnNullResponse_SetsLastResponseToNull()
	{
		ConnectionFeature feature = CreateDisconnectedFeature();

		await feature.Ping(TestContext.Current.CancellationToken);

		feature.LastResponse.ShouldBeNull();
	}

	// ── UTC timestamps ────────────────────────────────────────────────────────

	[Fact]
	public async Task Ping_SetsLastCheckedInUtc()
	{
		ConnectionFeature feature = CreateConnectedFeature();

		await feature.Ping(TestContext.Current.CancellationToken);

		feature.LastChecked.ShouldNotBeNull();
		feature.LastChecked!.Value.Kind.ShouldBe(DateTimeKind.Utc);
	}

	// ── Additional event coverage ─────────────────────────────────────────────

	[Fact]
	public async Task Ping_OnDisconnected_FiresStatusChangedEvent()
	{
		ConnectionFeature feature = CreateDisconnectedFeature();
		bool eventFired = false;
		feature.OnStatusChanged += () => eventFired = true;

		await feature.Ping(TestContext.Current.CancellationToken);

		eventFired.ShouldBeTrue();
	}

	// ── Checking transient state ──────────────────────────────────────────────

	[Fact]
	public async Task Ping_SlowResponse_SetsCheckingStatusBeforeCompletion()
	{
		TaskCompletionSource<PingResponse?> gate = new();
		List<ConnectionStatusEnum> observed = [];

		ConnectionFeature feature = CreateFeature(_ => gate.Task);
		feature.OnStatusChanged += () => observed.Add(feature.Status);

		Task pingTask = feature.Ping(TestContext.Current.CancellationToken);

		// Wait long enough for the 100ms delay to fire the Checking status
		await Task.Delay(200, TestContext.Current.CancellationToken);

		observed.ShouldContain(ConnectionStatusEnum.Checking);

		gate.SetResult(new PingResponse());
		await pingTask;

		feature.Status.ShouldBe(ConnectionStatusEnum.Connected);
	}

	[Fact]
	public async Task Ping_CancelledDuringChecking_RestoresPreviousStatus()
	{
		// First ping establishes Connected status
		int callCount = 0;
		CancellationTokenSource slowCts = new();
		TaskCompletionSource<PingResponse?> gate = new();

		ConnectionFeature feature = CreateFeature(ct =>
		{
			int count = Interlocked.Increment(ref callCount);
			if(count == 1)
			{
				return Task.FromResult<PingResponse?>(new PingResponse());
			}
			// Second call: slow, will be cancelled
			return gate.Task;
		});

		await feature.Ping(TestContext.Current.CancellationToken);
		feature.Status.ShouldBe(ConnectionStatusEnum.Connected);

		// Start second slow ping, wait for Checking state, then cancel
		Task secondPing = feature.Ping(slowCts.Token);
		await Task.Delay(200, TestContext.Current.CancellationToken);

		await slowCts.CancelAsync();
		gate.TrySetCanceled(TestContext.Current.CancellationToken);
		await secondPing;

		// Should restore to Connected (previous status), not stay on Checking
		feature.Status.ShouldBe(ConnectionStatusEnum.Connected);
	}

	// ── History defensive copy ────────────────────────────────────────────────

	[Fact]
	public async Task History_ReturnsCopy_NotLiveReference()
	{
		ConnectionFeature feature = CreateConnectedFeature();

		await feature.Ping(TestContext.Current.CancellationToken);
		IReadOnlyList<ConnectionCheckRecordModel> snapshot = feature.History;

		await feature.Ping(TestContext.Current.CancellationToken);

		// The snapshot should still have 1 entry (it's a copy)
		snapshot.Count.ShouldBe(1);
		// But the live history should now have 2
		feature.History.Count.ShouldBe(2);
	}
}
