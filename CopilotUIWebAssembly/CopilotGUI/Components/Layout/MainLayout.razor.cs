using CopilotGUI.Services;
using Microsoft.JSInterop;

namespace CopilotGUI.Components.Layout;

public partial class MainLayout : IDisposable
{
	static UIStateService? staticUIState;

	protected override async Task OnInitializedAsync()
	{
		await ThemeService.InitializeAsync();
		UIState.OnStateChanged += StateHasChanged;
		staticUIState = UIState; // Store static reference for title bar access
	}

	[JSInvokable("ToggleSettingsFromTitleBar")]
	public static void ToggleSettingsFromTitleBar()
	{
		staticUIState?.ToggleSettingsPopup();
	}

	public void Dispose()
	{
		Dispose(true);
		GC.SuppressFinalize(this);
	}

	protected virtual void Dispose(bool disposing)
	{
		if(disposing)
		{
			UIState.OnStateChanged -= StateHasChanged;
		}
	}
}