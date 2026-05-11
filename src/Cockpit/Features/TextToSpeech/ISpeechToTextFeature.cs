namespace Cockpit.Features.TextToSpeech;

/// <summary>
/// Abstracts platform speech-to-text, managing listening state and surfacing
/// recognition results as events so UI components stay decoupled from the
/// CommunityToolkit.Maui implementation.
/// </summary>
public interface ISpeechToTextFeature : IAsyncDisposable
{
	/// <summary>Raised whenever <see cref="IsListening"/> changes.</summary>
	event Action? OnStateChanged;

	/// <summary>Raised with each interim (partial) recognition result while listening.</summary>
	event EventHandler<string>? PartialResultReceived;

	/// <summary>Raised with the final recognised text when listening ends normally.</summary>
	event EventHandler<string>? FinalResultReceived;

	/// <summary>Raised when a permission denial or recognition failure occurs.</summary>
	event EventHandler<string>? ErrorReceived;

	bool IsListening { get; }

	/// <summary>
	/// Requests microphone permission and starts the platform listener.
	/// Returns <c>false</c> if permission was denied or the environment does not
	/// support packaged speech recognition.
	/// </summary>
	Task<bool> StartListeningAsync(CancellationToken cancellationToken = default);

	/// <summary>Stops the platform listener gracefully.</summary>
	Task StopListeningAsync(CancellationToken cancellationToken = default);
}
