namespace Cockpit.Controls;

public partial class ToolbarButton : ContentView
{
	public ToolbarButton()
	{
		InitializeComponent();
	}

	async void ContentButton_Clicked(object sender, EventArgs e)
	{
		await Browser.Default.OpenAsync("https://github.com/sponsors/IeuanWalker");
	}
}