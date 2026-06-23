using Cockpit.Features.Auth;
using Cockpit.Features.Sdk;
using Cockpit.UnitTests.Features.AppSettings;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Cockpit.UnitTests.Features.Auth;

public sealed class AuthCheckFeatureTests
{
	static CopilotClientFeature CreateClientFeature() =>
		new(NullLogger<CopilotClientFeature>.Instance, new UserAppSettings(new InMemoryPreferencesStorage()));

	static AuthCheckFeature CreateFeature(CopilotClientFeature clientFeature) =>
		new(clientFeature, NullLogger<AuthCheckFeature>.Instance);

	// ── Initial state ─────────────────────────────────────────────────────────

	[Fact]
	public async Task State_InitialValue_IsChecking()
	{
		await using CopilotClientFeature clientFeature = CreateClientFeature();
		AuthCheckFeature feature = CreateFeature(clientFeature);

		feature.State.ShouldBe(AuthState.Checking);
	}

	// ── GetClientAsync throws ─────────────────────────────────────────────────

	[Fact]
	public async Task CheckAuthAsync_WhenGetClientThrows_SetsStateToNotAuthenticated()
	{
		CopilotClientFeature clientFeature = CreateClientFeature();
		await clientFeature.DisposeAsync();

		AuthCheckFeature feature = CreateFeature(clientFeature);

		await feature.CheckAuthAsync(isRecheck: false, TestContext.Current.CancellationToken);

		feature.State.ShouldBe(AuthState.NotAuthenticated);
	}

	[Fact]
	public async Task CheckAuthAsync_WhenGetClientThrows_InvokesOnStateChanged()
	{
		CopilotClientFeature clientFeature = CreateClientFeature();
		await clientFeature.DisposeAsync();

		AuthCheckFeature feature = CreateFeature(clientFeature);

		int callCount = 0;
		feature.OnStateChanged += () => callCount++;

		await feature.CheckAuthAsync(isRecheck: false, TestContext.Current.CancellationToken);

		callCount.ShouldBe(2);
	}

	[Fact]
	public async Task CheckAuthAsync_WhenGetClientThrows_AlwaysInvokesOnStateChangedInFinally()
	{
		CopilotClientFeature clientFeature = CreateClientFeature();
		await clientFeature.DisposeAsync();

		AuthCheckFeature feature = CreateFeature(clientFeature);

		int callCount = 0;
		feature.OnStateChanged += () => callCount++;

		await feature.CheckAuthAsync(isRecheck: false, TestContext.Current.CancellationToken);

		callCount.ShouldBeGreaterThanOrEqualTo(1);
	}

	// ── Cancellation ──────────────────────────────────────────────────────────

	[Fact]
	public async Task CheckAuthAsync_WithCancelledToken_RethrowsOperationCanceledException()
	{
		await using CopilotClientFeature clientFeature = CreateClientFeature();
		AuthCheckFeature feature = CreateFeature(clientFeature);

		using CancellationTokenSource cts = new();
		await cts.CancelAsync();

		await Should.ThrowAsync<OperationCanceledException>(
			() => feature.CheckAuthAsync(isRecheck: false, cts.Token));
	}

	// ── Initial-check state sequencing ────────────────────────────────────────

	[Fact]
	public async Task CheckAuthAsync_InitialCheck_SetsCheckingStateBeforeOperation()
	{
		await using CopilotClientFeature clientFeature = CreateClientFeature();
		AuthCheckFeature feature = CreateFeature(clientFeature);

		AuthState? firstObservedState = null;
		int callCount = 0;
		feature.OnStateChanged += () =>
		{
			if (callCount == 0)
			{
				firstObservedState = feature.State;
			}
			callCount++;
		};

		using CancellationTokenSource cts = new();
		await cts.CancelAsync();

		try
		{
			await feature.CheckAuthAsync(isRecheck: false, cts.Token);
		}
		catch (OperationCanceledException) { }

		firstObservedState.ShouldBe(AuthState.Checking);
	}

	// ── Re-check does not flash Checking ─────────────────────────────────────

	[Fact]
	public async Task CheckAuthAsync_Recheck_DoesNotResetStateToChecking()
	{
		CopilotClientFeature clientFeature = CreateClientFeature();
		await clientFeature.DisposeAsync();

		AuthCheckFeature feature = CreateFeature(clientFeature);

		// Drive state to NotAuthenticated via an initial (non-recheck) call.
		await feature.CheckAuthAsync(isRecheck: false, TestContext.Current.CancellationToken);
		feature.State.ShouldBe(AuthState.NotAuthenticated);

		List<AuthState> observedStates = [];
		feature.OnStateChanged += () => observedStates.Add(feature.State);

		await feature.CheckAuthAsync(isRecheck: true, TestContext.Current.CancellationToken);

		observedStates.ShouldNotContain(AuthState.Checking);
	}
}
