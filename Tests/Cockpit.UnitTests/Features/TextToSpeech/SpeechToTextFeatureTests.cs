using Cockpit.Features.TextToSpeech;
using CommunityToolkit.Maui.Media;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Cockpit.UnitTests.Features.TextToSpeech;

// ---------------------------------------------------------------------------
// Fake ISpeechToText for unit tests
// ---------------------------------------------------------------------------

sealed class FakeSpeechToText : ISpeechToText
{
	public event EventHandler<SpeechToTextRecognitionResultUpdatedEventArgs>? RecognitionResultUpdated;
	public event EventHandler<SpeechToTextRecognitionResultCompletedEventArgs>? RecognitionResultCompleted;
	public event EventHandler<SpeechToTextStateChangedEventArgs>? StateChanged;

	public SpeechToTextState CurrentState { get; private set; } = SpeechToTextState.Stopped;
	public bool PermissionGranted { get; set; } = true;
	public bool IsListening => CurrentState == SpeechToTextState.Listening;

	public Task<bool> RequestPermissions(CancellationToken cancellationToken = default)
		=> Task.FromResult(PermissionGranted);

	public Task StartListenAsync(SpeechToTextOptions options, CancellationToken cancellationToken = default)
	{
		CurrentState = SpeechToTextState.Listening;
		return Task.CompletedTask;
	}

	public Task StopListenAsync(CancellationToken cancellationToken = default)
	{
		CurrentState = SpeechToTextState.Stopped;
		return Task.CompletedTask;
	}

	public void SimulatePartialResult(string result)
	{
		RecognitionResultUpdated?.Invoke(this, new SpeechToTextRecognitionResultUpdatedEventArgs(result));
	}

	public void SimulateCompletedSuccess(string text)
	{
		CurrentState = SpeechToTextState.Stopped;
		RecognitionResultCompleted?.Invoke(
			this,
			new SpeechToTextRecognitionResultCompletedEventArgs(new SpeechToTextResult(text, null)));
	}

	public void SimulateCompletedError(Exception exception)
	{
		CurrentState = SpeechToTextState.Stopped;
		RecognitionResultCompleted?.Invoke(
			this,
			new SpeechToTextRecognitionResultCompletedEventArgs(new SpeechToTextResult(null, exception)));
	}

	public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

// ---------------------------------------------------------------------------
// ThrowingSpeechToText — throws on StartListenAsync to test error recovery
// ---------------------------------------------------------------------------

sealed class ThrowingSpeechToText : ISpeechToText
{
#pragma warning disable CS0067 // Events are never used; required to satisfy interface
	public event EventHandler<SpeechToTextRecognitionResultUpdatedEventArgs>? RecognitionResultUpdated;
	public event EventHandler<SpeechToTextRecognitionResultCompletedEventArgs>? RecognitionResultCompleted;
	public event EventHandler<SpeechToTextStateChangedEventArgs>? StateChanged;

	public SpeechToTextState CurrentState { get; private set; } = SpeechToTextState.Stopped;
	public bool IsListening => CurrentState == SpeechToTextState.Listening;

	public Task<bool> RequestPermissions(CancellationToken cancellationToken = default)
		=> Task.FromResult(true);

	public Task StartListenAsync(SpeechToTextOptions options, CancellationToken cancellationToken = default)
		=> throw new InvalidOperationException("Simulated platform error");

	public Task StopListenAsync(CancellationToken cancellationToken = default)
		=> Task.CompletedTask;

	public ValueTask DisposeAsync() => ValueTask.CompletedTask;
#pragma warning restore CS0067
}

// ---------------------------------------------------------------------------
// SpeechToTextFeature tests
// ---------------------------------------------------------------------------

public class SpeechToTextFeatureTests
{
	static SpeechToTextFeature CreateFeature(FakeSpeechToText fake)
		=> new(fake, NullLogger<SpeechToTextFeature>.Instance);

	// -----------------------------------------------------------------------
	// StartListeningAsync
	// -----------------------------------------------------------------------

	[Fact]
	public async Task StartListeningAsync_GrantedPermission_ReturnsTrue()
	{
		FakeSpeechToText fake = new() { PermissionGranted = true };
		SpeechToTextFeature feature = CreateFeature(fake);

		bool result = await feature.StartListeningAsync(TestContext.Current.CancellationToken);

		result.ShouldBeTrue();
	}

	[Fact]
	public async Task StartListeningAsync_GrantedPermission_SetsIsListeningTrue()
	{
		FakeSpeechToText fake = new() { PermissionGranted = true };
		SpeechToTextFeature feature = CreateFeature(fake);

		await feature.StartListeningAsync(TestContext.Current.CancellationToken);

		feature.IsListening.ShouldBeTrue();
	}

	[Fact]
	public async Task StartListeningAsync_DeniedPermission_ReturnsFalse()
	{
		FakeSpeechToText fake = new() { PermissionGranted = false };
		SpeechToTextFeature feature = CreateFeature(fake);

		bool result = await feature.StartListeningAsync(TestContext.Current.CancellationToken);

		result.ShouldBeFalse();
	}

	[Fact]
	public async Task StartListeningAsync_DeniedPermission_DoesNotSetIsListening()
	{
		FakeSpeechToText fake = new() { PermissionGranted = false };
		SpeechToTextFeature feature = CreateFeature(fake);

		await feature.StartListeningAsync(TestContext.Current.CancellationToken);

		feature.IsListening.ShouldBeFalse();
	}

	[Fact]
	public async Task StartListeningAsync_DeniedPermission_FiresErrorReceived()
	{
		FakeSpeechToText fake = new() { PermissionGranted = false };
		SpeechToTextFeature feature = CreateFeature(fake);

		string? receivedError = null;
		feature.ErrorReceived += (_, msg) => receivedError = msg;

		await feature.StartListeningAsync(TestContext.Current.CancellationToken);

		receivedError.ShouldNotBeNullOrEmpty();
	}

	[Fact]
	public async Task StartListeningAsync_AlreadyListening_ReturnsFalseWithoutRestart()
	{
		FakeSpeechToText fake = new() { PermissionGranted = true };
		SpeechToTextFeature feature = CreateFeature(fake);

		await feature.StartListeningAsync(TestContext.Current.CancellationToken);
		bool secondResult = await feature.StartListeningAsync(TestContext.Current.CancellationToken);

		secondResult.ShouldBeFalse();
	}

	[Fact]
	public async Task StartListeningAsync_FiresOnStateChanged()
	{
		FakeSpeechToText fake = new() { PermissionGranted = true };
		SpeechToTextFeature feature = CreateFeature(fake);

		int eventCount = 0;
		feature.OnStateChanged += () => eventCount++;

		await feature.StartListeningAsync(TestContext.Current.CancellationToken);

		eventCount.ShouldBe(1);
	}

	// -----------------------------------------------------------------------
	// StopListeningAsync
	// -----------------------------------------------------------------------

	[Fact]
	public async Task StopListeningAsync_WhileListening_SetsIsListeningFalse()
	{
		FakeSpeechToText fake = new() { PermissionGranted = true };
		SpeechToTextFeature feature = CreateFeature(fake);

		await feature.StartListeningAsync(TestContext.Current.CancellationToken);
		await feature.StopListeningAsync(TestContext.Current.CancellationToken);

		feature.IsListening.ShouldBeFalse();
	}

	[Fact]
	public async Task StopListeningAsync_WhileListening_FiresOnStateChanged()
	{
		FakeSpeechToText fake = new() { PermissionGranted = true };
		SpeechToTextFeature feature = CreateFeature(fake);

		await feature.StartListeningAsync(TestContext.Current.CancellationToken);

		int eventCount = 0;
		feature.OnStateChanged += () => eventCount++;

		await feature.StopListeningAsync(TestContext.Current.CancellationToken);

		eventCount.ShouldBe(1);
	}

	[Fact]
	public async Task StopListeningAsync_WhenNotListening_DoesNotThrow()
	{
		FakeSpeechToText fake = new();
		SpeechToTextFeature feature = CreateFeature(fake);

		await feature.StopListeningAsync(TestContext.Current.CancellationToken);

		feature.IsListening.ShouldBeFalse();
	}

	[Fact]
	public async Task StopListeningAsync_WhenNotListening_DoesNotFireOnStateChanged()
	{
		FakeSpeechToText fake = new();
		SpeechToTextFeature feature = CreateFeature(fake);

		int eventCount = 0;
		feature.OnStateChanged += () => eventCount++;

		await feature.StopListeningAsync(TestContext.Current.CancellationToken);

		eventCount.ShouldBe(0);
	}

	// -----------------------------------------------------------------------
	// PartialResultReceived
	// -----------------------------------------------------------------------

	[Fact]
	public async Task PartialResultReceived_FiredWhenPlatformSendsInterimResult()
	{
		FakeSpeechToText fake = new() { PermissionGranted = true };
		SpeechToTextFeature feature = CreateFeature(fake);
		await feature.StartListeningAsync(TestContext.Current.CancellationToken);

		string? received = null;
		feature.PartialResultReceived += (_, text) => received = text;

		fake.SimulatePartialResult("hello");

		received.ShouldBe("hello");
	}

	// -----------------------------------------------------------------------
	// FinalResultReceived
	// -----------------------------------------------------------------------

	[Fact]
	public async Task FinalResultReceived_FiredOnSuccessfulCompletion()
	{
		FakeSpeechToText fake = new() { PermissionGranted = true };
		SpeechToTextFeature feature = CreateFeature(fake);
		await feature.StartListeningAsync(TestContext.Current.CancellationToken);

		string? received = null;
		feature.FinalResultReceived += (_, text) => received = text;

		fake.SimulateCompletedSuccess("hello world");

		received.ShouldBe("hello world");
	}

	[Fact]
	public async Task FinalResultReceived_ClearsIsListeningOnCompletion()
	{
		FakeSpeechToText fake = new() { PermissionGranted = true };
		SpeechToTextFeature feature = CreateFeature(fake);
		await feature.StartListeningAsync(TestContext.Current.CancellationToken);

		fake.SimulateCompletedSuccess("done");

		feature.IsListening.ShouldBeFalse();
	}

	[Fact]
	public async Task FinalResultReceived_FiresOnStateChangedOnCompletion()
	{
		FakeSpeechToText fake = new() { PermissionGranted = true };
		SpeechToTextFeature feature = CreateFeature(fake);
		await feature.StartListeningAsync(TestContext.Current.CancellationToken);

		int eventCount = 0;
		feature.OnStateChanged += () => eventCount++;

		fake.SimulateCompletedSuccess("done");

		eventCount.ShouldBe(1);
	}

	// -----------------------------------------------------------------------
	// ErrorReceived
	// -----------------------------------------------------------------------

	[Fact]
	public async Task ErrorReceived_FiredOnFailedCompletion()
	{
		FakeSpeechToText fake = new() { PermissionGranted = true };
		SpeechToTextFeature feature = CreateFeature(fake);
		await feature.StartListeningAsync(TestContext.Current.CancellationToken);

		string? receivedError = null;
		feature.ErrorReceived += (_, msg) => receivedError = msg;

		fake.SimulateCompletedError(new InvalidOperationException("recognition failed"));

		receivedError.ShouldBe("recognition failed");
	}

	[Fact]
	public async Task ErrorReceived_ClearsIsListeningOnFailure()
	{
		FakeSpeechToText fake = new() { PermissionGranted = true };
		SpeechToTextFeature feature = CreateFeature(fake);
		await feature.StartListeningAsync(TestContext.Current.CancellationToken);

		fake.SimulateCompletedError(new Exception("fail"));

		feature.IsListening.ShouldBeFalse();
	}

	// -----------------------------------------------------------------------
	// DisposeAsync
	// -----------------------------------------------------------------------

	[Fact]
	public async Task DisposeAsync_StopsListeningIfActive()
	{
		FakeSpeechToText fake = new() { PermissionGranted = true };
		SpeechToTextFeature feature = CreateFeature(fake);
		await feature.StartListeningAsync(TestContext.Current.CancellationToken);

		await feature.DisposeAsync();

		fake.IsListening.ShouldBeFalse();
	}

	[Fact]
	public async Task DisposeAsync_CanBeCalledMultipleTimes_WithoutException()
	{
		FakeSpeechToText fake = new();
		SpeechToTextFeature feature = CreateFeature(fake);

		await feature.DisposeAsync();
		await feature.DisposeAsync();
	}

	[Fact]
	public async Task StartListeningAsync_AfterDispose_ThrowsObjectDisposedException()
	{
		FakeSpeechToText fake = new() { PermissionGranted = true };
		SpeechToTextFeature feature = CreateFeature(fake);
		await feature.DisposeAsync();

		await Should.ThrowAsync<ObjectDisposedException>(() => feature.StartListeningAsync());
	}

	// -----------------------------------------------------------------------
	// ISpeechToTextFeature interface
	// -----------------------------------------------------------------------

	[Fact]
	public async Task StartListeningAsync_WhenStartListenThrows_ResetsIsListeningToFalse()
	{
		ThrowingSpeechToText fake = new();
		SpeechToTextFeature feature = new(fake, NullLogger<SpeechToTextFeature>.Instance);

		bool result = await feature.StartListeningAsync(TestContext.Current.CancellationToken);

		result.ShouldBeFalse();
		feature.IsListening.ShouldBeFalse();
	}

	[Fact]
	public async Task StartListeningAsync_WhenStartListenThrows_FiresOnStateChangedTwice()
	{
		ThrowingSpeechToText fake = new();
		SpeechToTextFeature feature = new(fake, NullLogger<SpeechToTextFeature>.Instance);

		int eventCount = 0;
		feature.OnStateChanged += () => eventCount++;

		await feature.StartListeningAsync(TestContext.Current.CancellationToken);

		// Once for IsListening = true, once for reset to false
		eventCount.ShouldBe(2);
	}

	[Fact]
	public async Task StartListeningAsync_WhenStartListenThrows_FiresErrorReceived()
	{
		ThrowingSpeechToText fake = new();
		SpeechToTextFeature feature = new(fake, NullLogger<SpeechToTextFeature>.Instance);

		string? receivedError = null;
		feature.ErrorReceived += (_, msg) => receivedError = msg;

		await feature.StartListeningAsync(TestContext.Current.CancellationToken);

		receivedError.ShouldNotBeNullOrEmpty();
	}
}
