namespace Cockpit.Features.TextToSpeech;

public interface ITextToSpeechFeature : IDisposable
{
	event Action? OnStateChanged;

	string? ActiveMessageId { get; }
	bool IsSpeaking { get; }

	Task<IEnumerable<Locale>> GetLocales();
	Task Speak(string messageId, string text);
	Task Stop();
}
