using Cockpit.Features.TextToSpeech;
using Cockpit.Services;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Pages.ChatPanel;

public partial class AgentTextToSpeechButton
{
	[Inject] TextToSpeechFeature _textToSpeachFeature { get; set; } = default!;
	[Inject] UnifiedSessionManager _sessionManager { get; set; } = default!;
	[Parameter] public string MessageId { get; set; } = string.Empty;
	[Parameter] public string Content { get; set; } = string.Empty;
	[Parameter] public bool Disabled { get; set; }

	string? _previousSessionId;

	protected override void OnInitialized()
	{
		_sessionManager.OnStateChanged += OnStateChanged;
		_textToSpeachFeature.OnStateChanged += OnTtsStateChanged;
		_previousSessionId = _sessionManager.CurrentSession?.Id;
	}

	void OnTtsStateChanged()
	{
		_ = InvokeAsync(StateHasChanged);
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
				await _textToSpeachFeature.Stop();
			}

			StateHasChanged();
		});
	}

	async Task OnClick()
	{
		await _textToSpeachFeature.Speak(MessageId, Content);
	}

	public void Dispose()
	{
		_sessionManager.OnStateChanged -= OnStateChanged;
		_textToSpeachFeature.OnStateChanged -= OnTtsStateChanged;
	}
}