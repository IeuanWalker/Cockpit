using Cockpit.Features.TextToSpeech;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Controls;

public partial class TextToSpeechButton
{
	[Inject] TextToSpeechFeature _textToSpeechFeature { get; set; } = default!;
	[Parameter] public string MessageId { get; set; } = string.Empty;
	[Parameter] public string Content { get; set; } = string.Empty;
	[Parameter] public bool Disabled { get; set; }

	async Task OnClick()
	{
		await _textToSpeechFeature.SpeakAsync(MessageId, Content);
	}
}