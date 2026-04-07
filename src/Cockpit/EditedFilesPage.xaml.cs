using Cockpit.Features.Splash;

namespace Cockpit;

public partial class EditedFilesPage : SecondaryWindowPage
{
	public EditedFilesPage(EditedFilesSplashFeature splashFeature)
	{
		InitializeComponent();
		InitializeSplash(splashOverlay, splashFeature);
	}
}
