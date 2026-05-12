using Cockpit.Features.Splash;
using Shouldly;

namespace Cockpit.UnitTests.Features.Splash;

// EditedFilesSplashFeature and LogViewerSplashFeature are marker subclasses with no
// additional logic. One representative test per subclass confirms the fire-once
// contract inherited from WindowSplashFeature.
public class SplashSubclassTests
{
	[Fact]
	public void EditedFilesSplashFeature_NotifyBlazorReady_FiresOnce()
	{
		EditedFilesSplashFeature feature = new();
		int callCount = 0;
		feature.OnBlazorReady += () => callCount++;

		feature.NotifyBlazorReady();
		feature.NotifyBlazorReady();

		callCount.ShouldBe(1);
	}

	[Fact]
	public void LogViewerSplashFeature_NotifyBlazorReady_FiresOnce()
	{
		LogViewerSplashFeature feature = new();
		int callCount = 0;
		feature.OnBlazorReady += () => callCount++;

		feature.NotifyBlazorReady();
		feature.NotifyBlazorReady();

		callCount.ShouldBe(1);
	}
}
