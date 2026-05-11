using Cockpit.Features.Splash;
using Shouldly;

namespace Cockpit.UnitTests.Features.Splash;

public class SplashSubclassTests
{
	[Fact]
	public void EditedFilesSplashFeature_IsWindowSplashFeature()
	{
		EditedFilesSplashFeature feature = new();

		feature.ShouldBeAssignableTo<WindowSplashFeature>();
	}

	[Fact]
	public void LogViewerSplashFeature_IsWindowSplashFeature()
	{
		LogViewerSplashFeature feature = new();

		feature.ShouldBeAssignableTo<WindowSplashFeature>();
	}

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
