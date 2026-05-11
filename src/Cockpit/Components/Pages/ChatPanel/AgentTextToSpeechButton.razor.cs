using Cockpit.Features.TextToSpeech;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Pages.ChatPanel;

public partial class AgentTextToSpeechButton : IDisposable
{
	[Parameter] public string MessageId { get; set; } = string.Empty;
	[Parameter] public string Content { get; set; } = string.Empty;
	[Parameter] public bool Disabled { get; set; }

	readonly ITextToSpeechFeature _textToSpeechFeature;

	// Cached value to skip re-renders when this button's active state hasn't changed.
	bool _isActive;

	public AgentTextToSpeechButton(ITextToSpeechFeature textToSpeechFeature)
	{
		_textToSpeechFeature = textToSpeechFeature;
	}

	protected override void OnInitialized()
	{
		_isActive = _textToSpeechFeature.ActiveMessageId == MessageId;
		_textToSpeechFeature.OnStateChanged += OnTtsStateChanged;
	}

	void OnTtsStateChanged()
	{
		bool isActive = _textToSpeechFeature.ActiveMessageId == MessageId;
		if(isActive == _isActive)
		{
			return;
		}

		_isActive = isActive;
		_ = InvokeAsync(StateHasChanged);
	}

	async Task OnClick()
	{
		await _textToSpeechFeature.Speak(MessageId, Content);
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
			_textToSpeechFeature.OnStateChanged -= OnTtsStateChanged;
		}
	}
}