using Cockpit.Services;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Popups.Settings;

public partial class VoiceSettings : ComponentBase, IDisposable
{
	[Inject] UIStateService _uiState { get; set; } = default!;

	protected override void OnInitialized()
	{
		_uiState.OnStateChanged += OnStateChanged;
	}

	void OnStateChanged()
	{
		InvokeAsync(StateHasChanged);
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
			_uiState.OnStateChanged -= OnStateChanged;
		}
	}
}