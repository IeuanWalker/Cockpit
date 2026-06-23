using Cockpit.Features.Splash;

namespace Cockpit;

public partial class TelemetryDashboardPage : SecondaryWindowPage
{
	public TelemetryDashboardPage(TelemetryDashboardSplashFeature splashFeature)
	{
		InitializeComponent();
		InitializeSplash(splashOverlay, splashFeature);
	}
}
