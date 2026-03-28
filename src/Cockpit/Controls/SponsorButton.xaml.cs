namespace Cockpit.Controls;

public partial class SponsorButton : ContentView
{
	public SponsorButton()
	{
		InitializeComponent();
	}

	async void ContentButton_Clicked(object sender, EventArgs e)
	{
		await Browser.Default.OpenAsync("https://github.com/sponsors/IeuanWalker");
	}
}