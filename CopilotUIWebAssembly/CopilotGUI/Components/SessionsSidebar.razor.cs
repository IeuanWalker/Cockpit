using CopilotGUI.Models;
using CopilotGUI.Services;
using Humanizer;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace CopilotGUI.Components;

public partial class SessionsSidebar : ComponentBase, IDisposable
{
	[Inject] TimestampService TimestampService { get; set; } = default!;

	DotNetObjectReference<SessionsSidebar>? _dotNetHelper;

	protected override void OnInitialized()
	{
		ChatService.OnSessionsChanged += StateHasChanged;
		UIState.OnStateChanged += StateHasChanged;
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
			_dotNetHelper = DotNetObjectReference.Create(this);
			await JSRuntime.InvokeVoidAsync("copilotUI.initializeResize", "leftResizeHandle", "leftSidebar", "left", _dotNetHelper);
		}
	}

	[JSInvokable]
	public void OnResize(int width)
	{
		UIState.SetLeftSidebarWidth(width);
	}

	async Task SelectSession(ChatSession session)
	{
		await ChatService.ResumeSessionAsync(session.Id);
	}

	void CreateNewSession()
	{
		ChatService.RequestNewSession();
	}

	static string GetSessionStatusClass(ChatSession session)
	{
		return session.Status switch
		{
			SessionStatus.AgentRunning => "",
			SessionStatus.AgentFinished => "",
			_ => "secondary-text"
		};
	}

	static string GetSessionStatusStyle(ChatSession session)
	{
		return session.Status switch
		{
			SessionStatus.AgentRunning => "color: #FFB900;",
			SessionStatus.AgentFinished => "color: #10893E;",
			_ => ""
		};
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
			ChatService.OnSessionsChanged -= StateHasChanged;
			UIState.OnStateChanged -= StateHasChanged;
			TimestampService.OnTick -= OnTimestampTick;
			_dotNetHelper?.Dispose();
		}
	}
}