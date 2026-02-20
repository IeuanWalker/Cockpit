using Cockpit.Features.TextToSpeech;
using Cockpit.Services;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Pages.ChatPanel;

public partial class ChatMessages : ComponentBase, IAsyncDisposable
{
	[Inject] UnifiedSessionManager _sessionManager { get; set; } = default!;
	[Inject] TextToSpeechFeature _ttsService { get; set; } = default!;
	[Inject] UIStateService _uiState { get; set; } = default!;

	string? _previousSessionId;

	protected override void OnInitialized()
	{
		_sessionManager.OnStateChanged += OnStateChanged;
		_ttsService.OnStateChanged += OnStateChanged;
		_uiState.OnStateChanged += OnStateChanged;
		_previousSessionId = _sessionManager.CurrentSession?.Id;
	}

	void OnStateChanged()
	{
		_ = InvokeAsync(async () =>
		{
			string? currentSessionId = _sessionManager.CurrentSession?.Id;
			if(currentSessionId != _previousSessionId)
			{
				_previousSessionId = currentSessionId;
				await _ttsService.StopAsync();
			}

			StateHasChanged();
		});
	}

	public async ValueTask DisposeAsync()
	{
		_sessionManager.OnStateChanged -= OnStateChanged;
		_ttsService.OnStateChanged -= OnStateChanged;
		_uiState.OnStateChanged -= OnStateChanged;

		await _ttsService.StopAsync();
	}
}
