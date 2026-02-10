using System.Globalization;
using Cockpit.Services;
using Cockpit.Services.Copilot.Models;
using CommunityToolkit.Maui.Media;
using GitHub.Copilot.SDK;
using Humanizer;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Cockpit.Components;

public partial class ChatWindow : ComponentBase, IDisposable
{
	[Inject] TimestampService TimestampService { get; set; } = default!;
	[Inject] ICopilotModelService ModelService { get; set; } = default!;
	[Inject] UIStateService UIState { get; set; } = default!;
	[Inject] ISpeechToText SpeechToText { get; set; } = default!;
	[Inject] ChatService ChatService { get; set; } = default!;
	[Inject] IJSRuntime JSRuntime { get; set; } = default!;

	WorkingDirectoryDialog? _workingDirectoryDialog;
	string _chatInput = string.Empty;
	ModelInfo? _selectedModel;
	string _selectedReasoningEffort = string.Empty;
	string? _pendingWorkingDirectory = null;
	List<ModelInfo> _availableModels = [];
	bool _shouldScrollToBottom = false;
	bool _isModelDropdownOpen = false;
	bool _isReasoningEffortDropdownOpen = false;

	protected override async Task OnInitializedAsync()
	{
		ChatService.OnMessagesChanged += OnMessagesChanged;
		ChatService.OnNewSessionRequested += OnNewSessionRequested;
		TimestampService.OnTick += OnTimestampTick;

		_availableModels = await ModelService.GetModels();
		if(_availableModels.Count > 0)
		{
			// TODO: Default model logic
			_selectedModel = _availableModels[0];
			UpdateReasoningEffortForSelectedModel();
		}

		// Load existing sessions from SDK
		await ChatService.LoadExistingSessionsAsync();

		SpeechToText.RecognitionResultCompleted += HandleRecognitionResultCompleted;
	}

	void OnNewSessionRequested()
	{
		InvokeAsync(CreateNewSession);
	}

	void OnTimestampTick()
	{
		InvokeAsync(StateHasChanged);
	}

	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		if(firstRender)
		{
			await UpdateInputBehavior();
			// Subscribe to UIState changes after first render to update input behavior
			UIState.OnStateChanged += OnUIStateChangedHandler;
		}

		if(_shouldScrollToBottom)
		{
			_shouldScrollToBottom = false;
			await ScrollToBottom();
		}
	}

	async void OnUIStateChangedHandler()
	{
		await UpdateInputBehavior();
		StateHasChanged();
	}

	async Task UpdateInputBehavior()
	{
		try
		{
			await JSRuntime.InvokeVoidAsync("cockpit.setupChatInputBehavior", "chatInput", UIState.EnterToSend);
		}
		catch
		{
			// Handle error silently
		}
	}

	void OnMessagesChanged()
	{
		_shouldScrollToBottom = true;
		StateHasChanged();
	}

	async Task ScrollToBottom()
	{
		try
		{
			await JSRuntime.InvokeVoidAsync("cockpit.scrollToBottom", "chatMessages");
		}
		catch
		{
			// Handle error silently
		}
	}

	async Task OnTextareaInput()
	{
		try
		{
			await JSRuntime.InvokeVoidAsync("cockpit.autoResizeTextarea", "chatInput");
		}
		catch
		{
			// Handle error silently
		}
	}

	async Task SendMessage()
	{
		if(string.IsNullOrWhiteSpace(_chatInput))
		{
			return;
		}

		string message = _chatInput.Trim();
		_chatInput = string.Empty;

		// Reset textarea height after clearing
		await Task.Delay(10);
		await OnTextareaInput();

		// Send via SDK
		await ChatService.SendMessageAsync(message);
	}

	async Task CreateNewSession()
	{
		try
		{
			// Open directory selection dialog
			_workingDirectoryDialog?.Open();
		}
		catch(Exception ex)
		{
			Console.Error.WriteLine($"Failed to open directory dialog: {ex.Message}");
		}
	}

	async Task OnWorkingDirectorySelected(string? directory)
	{
		if(string.IsNullOrEmpty(directory))
		{
			return;
		}

		try
		{
			_pendingWorkingDirectory = directory;
			await ChatService.CreateNewSessionAsync(_selectedModel, _selectedReasoningEffort, directory);
		}
		catch(Exception ex)
		{
			Console.Error.WriteLine($"Failed to create session: {ex.Message}");
		}
	}

	async Task HandleKeyDown(KeyboardEventArgs e)
	{
		if(e.Key == "Enter" && !e.ShiftKey && UIState.EnterToSend)
		{
			// Prevent default to avoid adding newline
			await SendMessage();
		}
	}

	static string GetTimeAgo(DateTimeOffset dateTime)
	{
		return dateTime.Humanize();
	}

	void ToggleModelDropdown()
	{
		_isModelDropdownOpen = !_isModelDropdownOpen;
	}

	void SelectModel(ModelInfo model)
	{
		_selectedModel = model;
		_isModelDropdownOpen = false;
		UpdateReasoningEffortForSelectedModel();
	}

	void UpdateReasoningEffortForSelectedModel()
	{
		if(_selectedModel is not null)
		{
			_selectedReasoningEffort = _selectedModel.DefaultReasoningEffort ?? string.Empty;
		}
	}

	void ToggleReasoningEffortDropdown()
	{
		_isReasoningEffortDropdownOpen = !_isReasoningEffortDropdownOpen;
	}

	void SelectReasoningEffort(string effort)
	{
		_selectedReasoningEffort = effort;
		_isReasoningEffortDropdownOpen = false;
	}

	string GetSelectedReasoningEffortDisplay()
	{
		if(string.IsNullOrEmpty(_selectedReasoningEffort))
		{
			return "Default";
		}

		return char.ToUpper(_selectedReasoningEffort[0]) + _selectedReasoningEffort[1..];
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
			ChatService.OnMessagesChanged -= OnMessagesChanged;
			ChatService.OnNewSessionRequested -= OnNewSessionRequested;
			UIState.OnStateChanged -= OnUIStateChangedHandler;
			TimestampService.OnTick -= OnTimestampTick;
		}
	}

	public async Task VoiceRecording()
	{
		if(!UIState.IsRecording)
		{
			await StartListening();
		}
		else
		{
			await StopListen();
		}

		UIState.ToggleRecording();

		async Task StartListening()
		{
			bool isGranted = await SpeechToText.RequestPermissions();
			if(!isGranted)
			{
				return;
			}

			SpeechToText.RecognitionResultUpdated += HandleRecognitionResultUpdated;
			await SpeechToText.StartListenAsync(new SpeechToTextOptions { Culture = CultureInfo.CurrentCulture, ShouldReportPartialResults = true }, CancellationToken.None);
		}

		Task StopListen()
		{
			SpeechToText.RecognitionResultUpdated -= HandleRecognitionResultUpdated;

			return SpeechToText.StopListenAsync(CancellationToken.None);
		}
	}

	void HandleRecognitionResultUpdated(object? sender, SpeechToTextRecognitionResultUpdatedEventArgs args)
	{
		_chatInput += args.RecognitionResult;
	}

	void HandleRecognitionResultCompleted(object? sender, SpeechToTextRecognitionResultCompletedEventArgs e)
	{
		_chatInput = e.RecognitionResult.IsSuccessful ? e.RecognitionResult.Text : e.RecognitionResult.Exception.Message;
	}
}