using Cockpit.Features.TextToSpeech;
using Cockpit.Services;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Pages.ChatPanel;

public partial class ChatMessages : ComponentBase, IDisposable
{
	[Inject] UnifiedSessionManager _sessionManager { get; set; } = default!;
	[Inject] TextToSpeechService _ttsService { get; set; } = default!;
	[Inject] UIStateService _uiState { get; set; } = default!;

	string? _previousSessionId;

	protected override void OnInitialized()
	{
		_sessionManager.OnStateChanged += OnStateChanged;
		_uiState.OnStateChanged += OnUIStateChanged;
		_previousSessionId = _sessionManager.CurrentSession?.Id;
	}

	void OnStateChanged()
	{
		_ = InvokeAsync(async () =>
		{
			// Stop TTS when the user switches to a different session
			string? currentSessionId = _sessionManager.CurrentSession?.Id;
			if(currentSessionId != _previousSessionId)
			{
				_previousSessionId = currentSessionId;
				await _ttsService.StopAsync();
			}

			StateHasChanged();
		});
	}

	void OnUIStateChanged()
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
			_sessionManager.OnStateChanged -= OnStateChanged;
			_uiState.OnStateChanged -= OnUIStateChanged;
		}
	}
}
