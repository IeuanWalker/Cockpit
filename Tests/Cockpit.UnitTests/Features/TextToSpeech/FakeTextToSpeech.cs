using Cockpit.Features.TextToSpeech;

namespace Cockpit.UnitTests.Features.TextToSpeech;

/// <summary>
/// Fake ITextToSpeech that blocks until CompleteCurrentSpeech() is called or
/// the cancellation token fires, exposing a semaphore to synchronise tests.
/// </summary>
sealed class FakeTextToSpeech : ITextToSpeech
{
	readonly SemaphoreSlim _speakStarted = new(0);
	volatile TaskCompletionSource<bool>? _currentTcs;

	public string? LastSpokenText { get; private set; }

	/// <summary>Unblocks the current SpeakAsync call normally.</summary>
	public void CompleteCurrentSpeech() => _currentTcs?.TrySetResult(true);

	/// <summary>Waits until SpeakAsync has been entered (and is blocking).</summary>
	public Task WaitForSpeakStartAsync(CancellationToken ct = default)
		=> _speakStarted.WaitAsync(ct);

	public Task<IEnumerable<Locale>> GetLocalesAsync()
		=> Task.FromResult<IEnumerable<Locale>>([]);

	public Task SpeakAsync(string text, SpeechOptions? options = null, CancellationToken cancelToken = default)
	{
		LastSpokenText = text;
		TaskCompletionSource<bool> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
		_currentTcs = tcs;
		_speakStarted.Release();
		cancelToken.Register(() => tcs.TrySetCanceled(cancelToken));
		return tcs.Task;
	}
}

/// <summary>
/// Test-only subclass that bypasses UserAppSettings / MAUI Preferences in BuildSpeechOptionsAsync.
/// </summary>
sealed class TestableTextToSpeechFeature : TextToSpeechFeature
{
	public TestableTextToSpeechFeature(ITextToSpeech textToSpeech) : base(textToSpeech) { }

	protected override Task<SpeechOptions> BuildSpeechOptionsAsync()
		=> Task.FromResult(new SpeechOptions());
}