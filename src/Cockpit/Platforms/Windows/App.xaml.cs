// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Cockpit.WinUI;
/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : MauiWinUIApplication
{
	/// <summary>
	/// Initializes the singleton application object. This is the first line of authored code executed, and as such is the
	/// logical equivalent of main() or WinMain().
	/// </summary>
	public App()
	{
		// Redirect WebView2 user data to %LocalAppData% so the app works when
		// installed to a read-only location such as Program Files.
		Environment.SetEnvironmentVariable(
			"WEBVIEW2_USER_DATA_FOLDER",
			Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
				"Cockpit", "WebView2"));

		this.InitializeComponent();
	}

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}