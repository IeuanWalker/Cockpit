using Cockpit.Features.TextToSpeech;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Pages.ChatPanel.InputArea;

public sealed partial class SpeechToTextControl : ComponentBase, IAsyncDisposable
{
	[Parameter] public string ChatInput { get; set; } = string.Empty;
	[Parameter] public EventCallback<string> ChatInputChanged { get; set; }
	[Parameter] public bool Disabled { get; set; }

	readonly ISpeechToTextFeature _speechToTextFeature;

	// Captures the text already in the input when recording begins so that
	// each partial-result update replaces only the in-progress spoken segment
	// rather than appending every interim token to the growing accumulation.
	string _textBeforeRecording = string.Empty;
	bool _disposed;

	public SpeechToTextControl(ISpeechToTextFeature speechToTextFeature)
	{
		_speechToTextFeature = speechToTextFeature;
	}

	protected override void OnInitialized()
	{
		_speechToTextFeature.OnStateChanged += OnSpeechToTextStateChanged;
		_speechToTextFeature.PartialResultReceived += OnPartialResultReceived;
		_speechToTextFeature.FinalResultReceived += OnFinalResultReceived;
		_speechToTextFeature.ErrorReceived += OnErrorReceived;
	}

	void OnSpeechToTextStateChanged()
	{
		_ = InvokeAsync(StateHasChanged);
	}

	public async Task VoiceRecording()
	{
		if(!_speechToTextFeature.IsListening)
		{
			_textBeforeRecording = ChatInput;
			await _speechToTextFeature.StartListeningAsync();
		}
		else
		{
			await _speechToTextFeature.StopListeningAsync();
		}
	}

	void OnPartialResultReceived(object? sender, string result)
	{
		_ = InvokeAsync(() => SetChatInput(_textBeforeRecording + result));
	}

	void OnFinalResultReceived(object? sender, string result)
	{
		_ = InvokeAsync(() => SetChatInput(_textBeforeRecording + result));
	}

	void OnErrorReceived(object? sender, string error)
	{
		_ = InvokeAsync(() => SetChatInput(error));
	}

	async Task SetChatInput(string value)
	{
		ChatInput = value;
		await ChatInputChanged.InvokeAsync(value);
	}

	public async ValueTask DisposeAsync()
	{
		if(_disposed)
		{
			return;
		}

		_disposed = true;

		_speechToTextFeature.OnStateChanged -= OnSpeechToTextStateChanged;
		_speechToTextFeature.PartialResultReceived -= OnPartialResultReceived;
		_speechToTextFeature.FinalResultReceived -= OnFinalResultReceived;
		_speechToTextFeature.ErrorReceived -= OnErrorReceived;

		try
		{
			await _speechToTextFeature.StopListeningAsync();
		}
		catch(Exception)
		{
		}
	}
}
