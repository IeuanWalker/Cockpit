using Cockpit.Features.Splash;
using Shouldly;

namespace Cockpit.UnitTests.Features.Splash;

public class SplashFeatureTests
{
	[Fact]
	public void SplashFeature_IsWindowSplashFeature()
	{
		SplashFeature feature = new();

		feature.ShouldBeAssignableTo<WindowSplashFeature>();
	}

	[Fact]
	public void NotifyBlazorReady_InvokesHandler()
	{
		SplashFeature feature = new();
		int callCount = 0;
		feature.OnBlazorReady += () => callCount++;

		feature.NotifyBlazorReady();

		callCount.ShouldBe(1);
	}

	[Fact]
	public void NotifyBlazorReady_FiresOnce_HandlerNotCalledOnSubsequentInvocations()
	{
		SplashFeature feature = new();
		int callCount = 0;
		feature.OnBlazorReady += () => callCount++;

		feature.NotifyBlazorReady();
		feature.NotifyBlazorReady();

		callCount.ShouldBe(1);
	}
}
