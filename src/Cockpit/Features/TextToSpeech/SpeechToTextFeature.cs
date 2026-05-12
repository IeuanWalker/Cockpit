using System.Globalization;
using CommunityToolkit.Maui.Media;
using Microsoft.Extensions.Logging;

namespace Cockpit.Features.TextToSpeech;

sealed class SpeechToTextFeature : ISpeechToTextFeature
{
	readonly ISpeechToText _speechToText;
	readonly ILogger<SpeechToTextFeature> _logger;
	volatile bool _disposed;

	public event Action? OnStateChanged;
	public event EventHandler<string>? PartialResultReceived;
	public event EventHandler<string>? FinalResultReceived;
	public event EventHandler<string>? ErrorReceived;

	public bool IsListening { get; private set; }

	public SpeechToTextFeature(ISpeechToText speechToText, ILogger<SpeechToTextFeature> logger)
	{
		_speechToText = speechToText;
		_logger = logger;
		_speechToText.RecognitionResultUpdated += HandleRecognitionResultUpdated;
		_speechToText.RecognitionResultCompleted += HandleRecognitionResultCompleted;
	}

	public async Task<bool> StartListeningAsync(CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);

		if(IsListening)
		{
			return false;
		}

		try
		{
			bool isGranted = await _speechToText.RequestPermissions(cancellationToken);
			if(!isGranted)
			{
				ErrorReceived?.Invoke(this, "Microphone permission was denied.");
				return false;
			}

			SpeechToTextOptions options = new()
			{
				Culture = CultureInfo.CurrentCulture,
				ShouldReportPartialResults = true,
			};

			IsListening = true;
			OnStateChanged?.Invoke();
			await _speechToText.StartListenAsync(options, cancellationToken);
			return true;
		}
		catch(FileNotFoundException ex) when((ex.FileName ?? string.Empty).EndsWith("AppxManifest.xml", StringComparison.OrdinalIgnoreCase))
		{
			IsListening = false;
			OnStateChanged?.Invoke();
			string msg = "Speech recognition requires the packaged MSIX app. Run using the 'MsixPackage' debug profile.";
			ErrorReceived?.Invoke(this, msg);
			return false;
		}
		catch(Exception ex)
		{
			IsListening = false;
			OnStateChanged?.Invoke();
			_logger.LogError(ex, "Error starting speech recognition");
			ErrorReceived?.Invoke(this, $"Error starting recording: {ex.Message}");
			return false;
		}
	}

	public async Task StopListeningAsync(CancellationToken cancellationToken = default)
	{
		if(!IsListening)
		{
			return;
		}

		try
		{
			await _speechToText.StopListenAsync(cancellationToken);
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Error stopping speech recognition");
			ErrorReceived?.Invoke(this, $"Error stopping recording: {ex.Message}");
		}
		finally
		{
			// RecognitionResultCompleted may have already flipped the flag; guard here.
			if(IsListening)
			{
				IsListening = false;
				OnStateChanged?.Invoke();
			}
		}
	}

	void HandleRecognitionResultUpdated(object? sender, SpeechToTextRecognitionResultUpdatedEventArgs args)
	{
		PartialResultReceived?.Invoke(this, args.RecognitionResult);
	}

	void HandleRecognitionResultCompleted(object? sender, SpeechToTextRecognitionResultCompletedEventArgs args)
	{
		bool wasListening = IsListening;
		IsListening = false;

		if(wasListening)
		{
			OnStateChanged?.Invoke();
		}

		if(args.RecognitionResult.IsSuccessful)
		{
			FinalResultReceived?.Invoke(this, args.RecognitionResult.Text ?? string.Empty);
		}
		else
		{
			string errorMessage = args.RecognitionResult.Exception?.Message ?? "Speech recognition failed";
			ErrorReceived?.Invoke(this, errorMessage);
		}
	}

	public async ValueTask DisposeAsync()
	{
		if(_disposed)
		{
			return;
		}

		_disposed = true;

		_speechToText.RecognitionResultUpdated -= HandleRecognitionResultUpdated;
		_speechToText.RecognitionResultCompleted -= HandleRecognitionResultCompleted;

		if(IsListening)
		{
			IsListening = false;
			try
			{
				await _speechToText.StopListenAsync(CancellationToken.None);
			}
			catch(Exception ex)
			{
				_logger.LogWarning(ex, "Error stopping speech recognition during disposal");
			}
		}
	}
}
