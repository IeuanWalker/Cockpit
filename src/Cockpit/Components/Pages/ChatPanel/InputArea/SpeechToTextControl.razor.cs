using System.Globalization;
using Cockpit.Features.UIState;
using CommunityToolkit.Maui.Media;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Pages.ChatPanel.InputArea;

public sealed partial class SpeechToTextControl : ComponentBase, IDisposable
{
	[Parameter] public string ChatInput { get; set; } = string.Empty;
	[Parameter] public EventCallback<string> ChatInputChanged { get; set; }
	[Parameter] public bool Disabled { get; set; }

	readonly UIStateFeature _uiStateFeature;
	readonly ISpeechToText _speechToText;
	public SpeechToTextControl(UIStateFeature uiStateFeature, ISpeechToText speechToText)
	{
		_uiStateFeature = uiStateFeature;
		_speechToText = speechToText;
	}

	bool _disposed;

	protected override void OnInitialized()
	{
		_speechToText.RecognitionResultCompleted += HandleRecognitionResultCompleted;
	}

	public async Task VoiceRecording()
	{
		if(!_uiStateFeature.IsRecording)
		{
			bool started = await StartListening();
			if(started)
			{
				_uiStateFeature.ToggleRecording();
			}
		}
		else
		{
			await StopListen();
			_uiStateFeature.ToggleRecording();
		}

		async Task<bool> StartListening()
		{
			try
			{
				bool isGranted = await _speechToText.RequestPermissions();
				if(!isGranted)
				{
					return false;
				}

				_speechToText.RecognitionResultUpdated += HandleRecognitionResultUpdated;
				await _speechToText.StartListenAsync(new SpeechToTextOptions { Culture = CultureInfo.CurrentCulture, ShouldReportPartialResults = true }, CancellationToken.None);
				return true;
			}
			catch(FileNotFoundException ex) when((ex.FileName ?? string.Empty).EndsWith("AppxManifest.xml", StringComparison.OrdinalIgnoreCase))
			{
				await SetChatInput("Speech recognition requires the packaged MSIX app. Run using the 'MsixPackage' debug profile.");
				return false;
			}
			catch(Exception ex)
			{
				await SetChatInput($"Error starting recording: {ex.Message}");
				return false;
			}
		}

		async Task StopListen()
		{
			try
			{
				_speechToText.RecognitionResultUpdated -= HandleRecognitionResultUpdated;
				await _speechToText.StopListenAsync(CancellationToken.None);
			}
			catch(Exception ex)
			{
				await SetChatInput($"Error stopping recording: {ex.Message}");
			}
		}
	}

	void HandleRecognitionResultUpdated(object? sender, SpeechToTextRecognitionResultUpdatedEventArgs args)
	{
		_ = InvokeAsync(async () =>
		{
			await SetChatInput(ChatInput + args.RecognitionResult);
		});
	}

	void HandleRecognitionResultCompleted(object? sender, SpeechToTextRecognitionResultCompletedEventArgs e)
	{
		_ = InvokeAsync(async () =>
		{
			await SetChatInput(e.RecognitionResult.IsSuccessful ? e.RecognitionResult.Text : e.RecognitionResult.Exception.Message);
		});
	}

	async Task SetChatInput(string value)
	{
		ChatInput = value;
		await ChatInputChanged.InvokeAsync(value);
	}

	void Dispose(bool disposing)
	{
		if(_disposed)
		{
			return;
		}

		if(disposing)
		{
			_speechToText.RecognitionResultUpdated -= HandleRecognitionResultUpdated;
			_speechToText.RecognitionResultCompleted -= HandleRecognitionResultCompleted;
		}

		_disposed = true;
	}

	public void Dispose()
	{
		Dispose(true);
		GC.SuppressFinalize(this);
	}
}
