namespace Cockpit.Controls;

public partial class SponsorButton : ContentView
{
	public SponsorButton()
	{
		InitializeComponent();
	}

	async void ContentButton_Clicked(object? sender, EventArgs e)
	{
		try
		{
			await Browser.Default.OpenAsync("https://github.com/sponsors/IeuanWalker");
		}
		catch(Exception)
		{
			// Ignore any exceptions that occur when trying to open the browser
		}
	}
}