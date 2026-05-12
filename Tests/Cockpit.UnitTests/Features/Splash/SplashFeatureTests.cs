using Cockpit.Features.Splash;
using Shouldly;

namespace Cockpit.UnitTests.Features.Splash;

// SplashFeature is a marker subclass of WindowSplashFeature with no additional logic.
// All behavioral tests live in WindowSplashFeatureTests. This file intentionally left minimal.
public class SplashFeatureTests
{
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
