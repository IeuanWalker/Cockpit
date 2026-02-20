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
				await _textToSpeachFeature.StopAsync();
			}

			StateHasChanged();
		});
	}

	async Task OnClick()
	{
		await _textToSpeachFeature.SpeakAsync(MessageId, Content);
	}

	public void Dispose()
	{
		_textToSpeachFeature.OnStateChanged -= OnStateChanged;
	}
}