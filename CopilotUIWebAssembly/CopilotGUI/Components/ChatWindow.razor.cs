using CopilotGUI.Services;
using Humanizer;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace CopilotGUI.Components;

public partial class ChatWindow : ComponentBase, IDisposable
{
	[Inject] TimestampService TimestampService { get; set; } = default!;

	string _chatInput = string.Empty;
	string _selectedModel = "GPT-4 Turbo";
	bool _shouldScrollToBottom = false;

	protected override void OnInitialized()
	{
		ChatService.OnMessagesChanged += OnMessagesChanged;
		TimestampService.OnTick += OnTimestampTick;
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
			await JSRuntime.InvokeVoidAsync("copilotUI.setupChatInputBehavior", "chatInput", UIState.EnterToSend);
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
			await JSRuntime.InvokeVoidAsync("copilotUI.scrollToBottom", "chatMessages");
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
			await JSRuntime.InvokeVoidAsync("copilotUI.autoResizeTextarea", "chatInput");
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

		ChatService.AddMessage(message, true);
		ChatService.AddTypingIndicator();

		// Simulate AI response
		await Task.Delay(2000);
		ChatService.RemoveTypingIndicator();
		ChatService.AddMessage("This is a simulated response. In a real implementation, this would call your AI backend.", false);
	}

	async Task HandleKeyDown(KeyboardEventArgs e)
	{
		if(e.Key == "Enter" && !e.ShiftKey && UIState.EnterToSend)
		{
			// Prevent default to avoid adding newline
			await SendMessage();
		}
	}

	static string GetTimeAgo(DateTime dateTime)
	{
		return dateTime.Humanize();
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
}