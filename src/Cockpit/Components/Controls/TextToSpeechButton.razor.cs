using Cockpit.Features.TextToSpeech;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Controls;

public partial class TextToSpeechButton
{
	[Inject] TextToSpeechFeature _textToSpeachFeature { get; set; } = default!;
	[Parameter] public string MessageId { get; set; } = string.Empty;
	[Parameter] public string Content { get; set; } = string.Empty;
	[Parameter] public bool Disabled { get; set; }

	async Task OnClick()
	{
		await _textToSpeachFeature.SpeakAsync(MessageId, Content);
	}

	protected override void OnInitialized()
	{
		_textToSpeachFeature.OnStateChanged += OnTtsStateChanged;
	}
	void OnTtsStateChanged() => InvokeAsync(StateHasChanged);
	public void Dispose()
	{
		_textToSpeachFeature.OnStateChanged -= OnTtsStateChanged;
	}
}