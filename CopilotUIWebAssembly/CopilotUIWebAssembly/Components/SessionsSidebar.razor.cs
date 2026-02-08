using CopilotUIWebAssembly.Models;
using CopilotUIWebAssembly.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace CopilotUIWebAssembly.Components;

public partial class SessionsSidebar : ComponentBase, IDisposable
{
	DotNetObjectReference<SessionsSidebar>? _dotNetHelper;

	protected override void OnInitialized()
	{
		ChatService.OnSessionsChanged += StateHasChanged;
		UIState.OnStateChanged += StateHasChanged;
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

	void SelectSession(ChatSession session)
	{
		ChatService.SetCurrentSession(session);
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
			SessionStatus.AgentRunning => "color: var(--accent-color);",
			SessionStatus.AgentFinished => "color: #10893E;",
			_ => ""
		};
	}

	static string GetTimeAgo(DateTime dateTime)
	{
		TimeSpan timeSpan = DateTime.Now - dateTime;

		if(timeSpan.TotalMinutes < 1)
		{
			return "Just now";
		}

		if(timeSpan.TotalMinutes < 60)
		{
			return $"{(int)timeSpan.TotalMinutes} min ago";
		}

		if(timeSpan.TotalHours < 24)
		{
			return $"{(int)timeSpan.TotalHours} hours ago";
		}

		if(timeSpan.TotalDays < 7)
		{
			return $"{(int)timeSpan.TotalDays} days ago";
		}

		return "Last week";
	}

	public void Dispose()
	{
		Dispose(true);
		GC.SuppressFinalize(this);
	}

	protected virtual void Dispose(bool disposing)
	{
		if (disposing)
		{
			ChatService.OnSessionsChanged -= StateHasChanged;
			UIState.OnStateChanged -= StateHasChanged;
			_dotNetHelper?.Dispose();
		}
	}
}