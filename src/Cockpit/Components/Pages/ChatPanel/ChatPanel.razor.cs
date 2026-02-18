using System.Globalization;
using Cockpit.Features.Permissions;
using Cockpit.Features.Permissions.Models;
using Cockpit.Services;
using CommunityToolkit.Maui.Media;
using GitHub.Copilot.SDK;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Cockpit.Components.Pages.ChatPanel;

public partial class ChatPanel : ComponentBase, IAsyncDisposable
{
	[Inject] TimestampService TimestampService { get; set; } = default!;
	[Inject] CopilotModelService ModelService { get; set; } = default!;
	[Inject] UIStateService UIState { get; set; } = default!;
	[Inject] ISpeechToText SpeechToText { get; set; } = default!;
	[Inject] UnifiedSessionManager SessionManager { get; set; } = default!;
	[Inject] PermissionFeature PermissionFeature { get; set; } = default!;
	[Inject] IJSRuntime JSRuntime { get; set; } = default!;
	[Inject] ILogger<Main> Logger { get; set; } = default!;

	string _chatInput = string.Empty;
	List<ModelInfo> _availableModels = [];
	bool _shouldScrollToBottom = false;
	bool _isModelDropdownOpen = false;
	bool _isReasoningEffortDropdownOpen = false;
	bool _isUserScrolledUpFromChat = false;
	int _lastMessageCount = 0;
	DotNetObjectReference<ChatPanel>? _dotNetRef;

	// Helper property to safely get the first pending request
	PermissionRequestModel? FirstPendingRequest => SessionManager.CurrentSession?.PendingPermissionRequests?.Values.FirstOrDefault();

	protected override async Task OnInitializedAsync()
	{
		SessionManager.OnStateChanged += OnStateChanged;
		TimestampService.OnTick += OnTimestampTick;

		_availableModels = await ModelService.GetModels();
		if(_availableModels.Count > 0)
		{
			// TODO: Default model logic
			SessionManager.CurrentSession?.Model = _availableModels[0];
			UpdateReasoningEffortForSelectedModel();
		}

		// Load existing sessions from SDK
		await SessionManager.LoadExistingSessionsAsync();

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

			// Setup smart scroll tracking
			_dotNetRef = DotNetObjectReference.Create(this);
			await SetupSmartScroll();

			// Initialize message count
			_lastMessageCount = SessionManager.CurrentSession?.Messages?.Count ?? 0;
		}

		if(_shouldScrollToBottom && !_isUserScrolledUpFromChat)
		{
			_shouldScrollToBottom = false;
			await ScrollToBottom();
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

	void OnStateChanged()
	{
		// Only scroll chat if there's a new message
		int currentMessageCount = SessionManager.CurrentSession?.Messages?.Count ?? 0;
		if(currentMessageCount > _lastMessageCount)
		{
			_shouldScrollToBottom = true;
			_lastMessageCount = currentMessageCount;
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

	async Task SetupSmartScroll()
	{
		try
		{
			await JSRuntime.InvokeVoidAsync("cockpit.setupSmartScroll", "chatMessages", _dotNetRef, "OnChatScrollPositionChanged");
		}
		catch
		{
			// Handle error silently
		}
	}

	[JSInvokable]
	public void OnChatScrollPositionChanged(bool isNearBottom)
	{
		_isUserScrolledUpFromChat = !isNearBottom;
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

		// Reset scroll state when user sends a message - we want to auto-scroll
		_isUserScrolledUpFromChat = false;
		_shouldScrollToBottom = true; // Force immediate scroll

		// Send via SDK
		await SessionManager.SendMessageAsync(message);
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
		if(SessionManager.CurrentSession is null)
		{
			return;
		}

		// Check if model actually changed
		if(SessionManager.CurrentSession.Model.Id == model.Id)
		{
			_isModelDropdownOpen = false;
			return;
		}

		// Update model and mark session for restart
		SessionManager.CurrentSession.Model = model;
		SessionManager.CurrentSession.RequiresRestart = true;

		_isModelDropdownOpen = false;

		// Update reasoning effort based on new model's defaults
		UpdateReasoningEffortForSelectedModel();
	}

	void UpdateReasoningEffortForSelectedModel()
	{
		if(SessionManager.CurrentSession?.Model is null)
		{
			return;
		}

		string newEffort = SessionManager.CurrentSession.Model.DefaultReasoningEffort ?? string.Empty;

		// Only mark for restart if reasoning effort actually changed
		if(SessionManager.CurrentSession.ReasoningEffort != newEffort)
		{
			SessionManager.CurrentSession.ReasoningEffort = newEffort;
			SessionManager.CurrentSession.RequiresRestart = true;
		}
	}

	void ToggleReasoningEffortDropdown()
	{
		_isReasoningEffortDropdownOpen = !_isReasoningEffortDropdownOpen;
	}

	void SelectReasoningEffort(string effort)
	{
		if(SessionManager.CurrentSession is null)
		{
			return;
		}

		// Check if reasoning effort actually changed
		if(SessionManager.CurrentSession.ReasoningEffort == effort)
		{
			_isReasoningEffortDropdownOpen = false;
			return;
		}

		// Update reasoning effort and mark session for restart
		SessionManager.CurrentSession.ReasoningEffort = effort;
		SessionManager.CurrentSession.RequiresRestart = true;

		_isReasoningEffortDropdownOpen = false;
	}

	string GetSelectedReasoningEffortDisplay()
	{
		if(string.IsNullOrEmpty(SessionManager.CurrentSession?.ReasoningEffort))
		{
			return "Default";
		}

		return char.ToUpper(SessionManager.CurrentSession.ReasoningEffort[0]) + SessionManager.CurrentSession.ReasoningEffort[1..];
	}

	string GetDisplayModelName()
	{
		if(SessionManager.CurrentSession is null)
		{
			return "No Model";
		}

		return SessionManager.CurrentSession.Model.Name;
	}

	double GetDisplayModelMultiplier()
	{
		if(SessionManager.CurrentSession is null)
		{
			return 1.0;
		}

		return SessionManager.CurrentSession.Model.Billing?.Multiplier ?? 1.0;
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
			return "#00d000";
		}
		else if(multiplier >= maxMultiplier)
		{
			return "#FF0000";
		}
		else if(multiplier > 1)
		{
			return "#ff8c00";
		}
		else
		{
			return "#999999";
		}
	}

	void ToggleYoloMode()
	{
		if(SessionManager.CurrentSession is null)
		{
			return;
		}

		SessionManager.CurrentSession.IsYolo = !SessionManager.CurrentSession.IsYolo;
		StateHasChanged();
	}

	string GetYoloButtonStyle()
	{
		if(SessionManager.CurrentSession?.IsYolo == true)
		{
			return "background-color: var(--accent-color); color: white; border: 1px solid var(--accent-color);";
		}
		return "color: var(--text-color); border: 1px solid var(--input-border); background-color: var(--input-bg);";
	}

	void ToggleTerminalPanel()
	{
		if(SessionManager.CurrentSession is not null)
		{
			SessionManager.CurrentSession.IsTerminalOpen = !SessionManager.CurrentSession.IsTerminalOpen;
			StateHasChanged();
		}
	}

	public async ValueTask DisposeAsync()
	{
		SessionManager.OnStateChanged -= OnStateChanged;
		UIState.OnStateChanged -= OnUIStateChangedHandler;
		TimestampService.OnTick -= OnTimestampTick;

		// Cleanup smart scroll
		try
		{
			await JSRuntime.InvokeVoidAsync("cockpit.cleanupSmartScroll", "chatMessages");
		}
		catch(Exception ex)
		{
			Logger.LogDebug(ex, "Failed to cleanup smart scroll for chat messages");
		}

		_dotNetRef?.Dispose();
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

	// Handle permission decision from the PermissionRequestPanel
	void HandlePermissionDecision(PermissionDecisionEnum decision)
	{
		PermissionRequestModel? currentRequest = FirstPendingRequest;
		if(currentRequest is null)
		{
			Logger.LogWarning("HandlePermissionDecision called but CurrentSession is null");
			return;
		}

		PermissionFeature.ResolvePermissionRequest(currentRequest.Id, decision);
	}
}