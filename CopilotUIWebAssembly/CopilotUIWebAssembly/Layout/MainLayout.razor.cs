using CopilotUIWebAssembly.Services;

namespace CopilotUIWebAssembly.Layout;

public partial class MainLayout : IDisposable
{
	protected override async Task OnInitializedAsync()
	{
		await ThemeService.InitializeAsync();
		UIState.OnStateChanged += StateHasChanged;
	}

	public void Dispose()
	{
		Dispose(true);
		GC.SuppressFinalize(this);
	}

	protected virtual void Dispose(bool disposing)
	{
		if (disposing)
		{
			UIState.OnStateChanged -= StateHasChanged;
		}
	}
}