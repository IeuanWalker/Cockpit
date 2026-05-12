using Cockpit.Features.Splash;
using Shouldly;

namespace Cockpit.UnitTests.Features.Splash;

public class WindowSplashFeatureTests
{
	// Concrete subclass used to exercise the abstract base
	sealed class TestSplashFeature : WindowSplashFeature { }

	[Fact]
	public void NotifyBlazorReady_InvokesSubscribedHandler()
	{
		TestSplashFeature feature = new();
		int callCount = 0;
		feature.OnBlazorReady += () => callCount++;

		feature.NotifyBlazorReady();

		callCount.ShouldBe(1);
	}

	[Fact]
	public void NotifyBlazorReady_InvokesAllSubscribedHandlers()
	{
		TestSplashFeature feature = new();
		int callCount = 0;
		feature.OnBlazorReady += () => callCount++;
		feature.OnBlazorReady += () => callCount++;

		feature.NotifyBlazorReady();

		callCount.ShouldBe(2);
	}

	[Fact]
	public void NotifyBlazorReady_WhenNoHandlers_DoesNotThrow()
	{
		TestSplashFeature feature = new();

		Should.NotThrow(() => feature.NotifyBlazorReady());
	}

	[Fact]
	public void NotifyBlazorReady_ClearsHandlersAfterInvoke()
	{
		TestSplashFeature feature = new();
		int callCount = 0;
		feature.OnBlazorReady += () => callCount++;

		feature.NotifyBlazorReady();
		feature.NotifyBlazorReady(); // second call — handler must not fire again

		callCount.ShouldBe(1);
	}

	[Fact]
	public void UnsubscribeAfterFire_DoesNotThrow()
	{
		TestSplashFeature feature = new();
		static void Handler() { }

		feature.OnBlazorReady += Handler;
		feature.NotifyBlazorReady(); // clears handlers
		Should.NotThrow(() => feature.OnBlazorReady -= Handler); // unsubscribe from null event
	}

	[Fact]
	public void SubscribeAfterFire_HandlerInvokedOnNextCall()
	{
		TestSplashFeature feature = new();
		feature.NotifyBlazorReady(); // first call — no handlers registered

		int callCount = 0;
		feature.OnBlazorReady += () => callCount++;
		feature.NotifyBlazorReady(); // second call — new handler should fire once

		callCount.ShouldBe(1);
	}

	[Fact]
	public async Task NotifyBlazorReady_CalledConcurrently_HandlerInvokedExactlyOnce()
	{
		TestSplashFeature feature = new();
		int callCount = 0;
		feature.OnBlazorReady += () => Interlocked.Increment(ref callCount);

		CancellationToken ct = TestContext.Current.CancellationToken;
		Task t1 = Task.Run(() => feature.NotifyBlazorReady(), ct);
		Task t2 = Task.Run(() => feature.NotifyBlazorReady(), ct);
		await Task.WhenAll(t1, t2);

		callCount.ShouldBe(1);
	}

	[Fact]
	public void NotifyBlazorReady_WhenFirstHandlerThrows_ExceptionPropagates_AndSecondHandlerDoesNotRun()
	{
		// Documents multicast delegate behaviour: an exception in one handler
		// stops subsequent handlers from running and propagates to the caller.
		TestSplashFeature feature = new();
		int secondCallCount = 0;
		feature.OnBlazorReady += () => throw new InvalidOperationException("first");
		feature.OnBlazorReady += () => secondCallCount++;

		Should.Throw<InvalidOperationException>(() => feature.NotifyBlazorReady());
		secondCallCount.ShouldBe(0);
	}
}
