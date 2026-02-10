using Cockpit.Services;
using Microsoft.JSInterop;

namespace Cockpit.Components.Layout;

public partial class MainLayout : IDisposable
{
	static UIStateService? staticUIState;

	protected override async Task OnInitializedAsync()
	{
		await ThemeService.InitializeAsync();
		UIState.OnStateChanged += OnStateChanged;
		ChatService.OnSessionsChanged += OnStateChanged;
		ChatService.OnMessagesChanged += OnStateChanged;
		staticUIState = UIState; // Store static reference for title bar access
	}

	void OnStateChanged()
	{
		InvokeAsync(StateHasChanged);
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
			UIState.OnStateChanged -= OnStateChanged;
			ChatService.OnSessionsChanged -= OnStateChanged;
			ChatService.OnMessagesChanged -= OnStateChanged;
		}
	}
}