using System.Globalization;
using Cockpit.Services;
using Cockpit.Services.Copilot;
using CommunityToolkit.Maui.Media;
using GitHub.Copilot.SDK;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Cockpit.Components;

public partial class Main : ComponentBase, IDisposable
{
	[Inject] TimestampService TimestampService { get; set; } = default!;
	[Inject] CopilotModelService ModelService { get; set; } = default!;
	[Inject] UIStateService UIState { get; set; } = default!;
	[Inject] ISpeechToText SpeechToText { get; set; } = default!;
	[Inject] ChatService ChatService { get; set; } = default!;
	[Inject] IJSRuntime JSRuntime { get; set; } = default!;

	string _chatInput = string.Empty;
	List<ModelInfo> _availableModels = [];
	bool _shouldScrollToBottom = false;
	bool _shouldScrollThinkingPanel = false;
	bool _isModelDropdownOpen = false;
	bool _isReasoningEffortDropdownOpen = false;

	protected override async Task OnInitializedAsync()
	{
		ChatService.OnMessagesChanged += OnMessagesChanged;
		TimestampService.OnTick += OnTimestampTick;

		_availableModels = await ModelService.GetModels();
		if(_availableModels.Count > 0)
		{
			// TODO: Default model logic
			ChatService.CurrentSession?.Model = _availableModels[0];
			UpdateReasoningEffortForSelectedModel();
		}

		// Load existing sessions from SDK
		await ChatService.LoadExistingSessionsAsync();

		SpeechToText.RecognitionResultCompleted += HandleRecognitionResultCompleted;
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

		if(_shouldScrollThinkingPanel)
		{
			_shouldScrollThinkingPanel = false;
			await ScrollThinkingPanelToBottom();
		}
	}

	void OnUIStateChangedHandler()
	{
		InvokeAsync(async () =>
		{
			await UpdateInputBehavior();
			StateHasChanged();
		});
	}

	async Task UpdateInputBehavior()
	{
		try
		{
			await JSRuntime.InvokeVoidAsync("cockpit.setupChatInputBehavior", "chatInput", UIState.SendOnEnter);
		}
		catch
		{
			// Handle error silently
		}
	}

	void OnMessagesChanged()
	{
		_shouldScrollToBottom = true;
		// Also scroll thinking panel if it's visible and has content
		if(ChatService.IsThinking && ChatService.ActiveThinkingGroup?.Tools.Any() == true)
		{
			_shouldScrollThinkingPanel = true;
		}
		InvokeAsync(StateHasChanged);
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

	async Task ScrollThinkingPanelToBottom()
	{
		try
		{
			await JSRuntime.InvokeVoidAsync("cockpit.scrollToBottom", "workingContent");
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

	async Task HandleKeyDown(KeyboardEventArgs e)
	{
		if(e.Key == "Enter" && !e.ShiftKey && UIState.SendOnEnter)
		{
			// Prevent default to avoid adding newline
			await SendMessage();
		}
	}

	void ToggleModelDropdown()
	{
		_isModelDropdownOpen = !_isModelDropdownOpen;
	}

	void SelectModel(ModelInfo model)
	{
		if(ChatService.CurrentSession is null)
		{
			return;
		}

		// Check if model actually changed
		if(ChatService.CurrentSession.Model.Id == model.Id)
		{
			_isModelDropdownOpen = false;
			return;
		}

		// Update model and mark session for restart
		ChatService.CurrentSession.Model = model;
		ChatService.CurrentSession.RequiresRestart = true;

		_isModelDropdownOpen = false;

		// Update reasoning effort based on new model's defaults
		UpdateReasoningEffortForSelectedModel();
	}

	void UpdateReasoningEffortForSelectedModel()
	{
		if(ChatService.CurrentSession?.Model is null)
		{
			return;
		}

		string newEffort = ChatService.CurrentSession.Model.DefaultReasoningEffort ?? string.Empty;

		// Only mark for restart if reasoning effort actually changed
		if(ChatService.CurrentSession.ReasoningEffort != newEffort)
		{
			ChatService.CurrentSession.ReasoningEffort = newEffort;
			ChatService.CurrentSession.RequiresRestart = true;
		}
	}

	void ToggleReasoningEffortDropdown()
	{
		_isReasoningEffortDropdownOpen = !_isReasoningEffortDropdownOpen;
	}

	void SelectReasoningEffort(string effort)
	{
		if(ChatService.CurrentSession is null)
		{
			return;
		}

		// Check if reasoning effort actually changed
		if(ChatService.CurrentSession.ReasoningEffort == effort)
		{
			_isReasoningEffortDropdownOpen = false;
			return;
		}

		// Update reasoning effort and mark session for restart
		ChatService.CurrentSession.ReasoningEffort = effort;
		ChatService.CurrentSession.RequiresRestart = true;

		_isReasoningEffortDropdownOpen = false;
	}

	string GetSelectedReasoningEffortDisplay()
	{
		if(string.IsNullOrEmpty(ChatService.CurrentSession?.ReasoningEffort))
		{
			return "Default";
		}

		return char.ToUpper(ChatService.CurrentSession.ReasoningEffort[0]) + ChatService.CurrentSession.ReasoningEffort[1..];
	}

	string GetDisplayModelName()
	{
		if(ChatService.CurrentSession is null)
		{
			return "No Model";
		}

		return ChatService.CurrentSession.Model.Name;
	}

	double GetDisplayModelMultiplier()
	{
		if(ChatService.CurrentSession is null)
		{
			return 1.0;
		}

		return ChatService.CurrentSession.Model.Billing?.Multiplier ?? 1.0;
	}

	string GetMultiplierColor(double multiplier)
	{
		double maxMultiplier = _availableModels.Max(m => m.Billing?.Multiplier ?? 0);

		if(multiplier == 0)
		{
			return "#00ff00";
		}
		else if(multiplier < 1)
		{
			return "#90ee90";
		}
		else if(multiplier == 1)
		{
			return "var(--text-color)";
		}
		else if(multiplier > 2 && multiplier >= maxMultiplier)
		{
			return "#ff4444";
		}
		else if(multiplier > 1)
		{
			return "#ffa500";
		}

		return "var(--text-color)";
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
			UIState.OnStateChanged -= OnUIStateChangedHandler;
			TimestampService.OnTick -= OnTimestampTick;
		}
	}

	public async Task VoiceRecording()
	{
		if(!UIState.IsRecording)
		{
			bool started = await StartListening();
			if(started)
			{
				UIState.ToggleRecording();
			}
		}
		else
		{
			await StopListen();
			UIState.ToggleRecording();
		}

		async Task<bool> StartListening()
		{
			try
			{
				bool isGranted = await SpeechToText.RequestPermissions();
				if(!isGranted)
				{
					return false;
				}

				SpeechToText.RecognitionResultUpdated += HandleRecognitionResultUpdated;
				await SpeechToText.StartListenAsync(new SpeechToTextOptions { Culture = CultureInfo.CurrentCulture, ShouldReportPartialResults = true }, CancellationToken.None);
				return true;
			}
			catch(FileNotFoundException ex) when((ex.FileName ?? string.Empty).EndsWith("AppxManifest.xml", StringComparison.OrdinalIgnoreCase))
			{
				_chatInput = "Speech recognition requires the packaged MSIX app. Run using the 'MsixPackage' debug profile.";
				await InvokeAsync(StateHasChanged);
				return false;
			}
			catch(Exception ex)
			{
				_chatInput = $"Error starting recording: {ex.Message}";
				await InvokeAsync(StateHasChanged);
				return false;
			}
		}

		async Task StopListen()
		{
			try
			{
				SpeechToText.RecognitionResultUpdated -= HandleRecognitionResultUpdated;
				await SpeechToText.StopListenAsync(CancellationToken.None);
			}
			catch(Exception ex)
			{
				_chatInput = $"Error stopping recording: {ex.Message}";
				await InvokeAsync(StateHasChanged);
			}
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