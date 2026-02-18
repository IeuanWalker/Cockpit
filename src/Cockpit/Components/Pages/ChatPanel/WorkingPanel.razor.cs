using Cockpit.Models;
using Cockpit.Services;
using Humanizer;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Cockpit.Components.Pages.ChatPanel;

public sealed partial class WorkingPanel : IAsyncDisposable
{
	[Parameter]
	public ActivityGroup? Group { get; set; }

	[Parameter]
	public bool IsVisible { get; set; }

	[Inject]
	UnifiedSessionManager SessionManager { get; set; } = default!;

	[Inject]
	IJSRuntime JSRuntime { get; set; } = default!;

	[Inject]
	ILogger<WorkingPanel> Logger { get; set; } = default!;

	Timer? _timer;
	bool _isUserScrolledUpFromWorking = false;
	bool _shouldScrollToBottom = false;
	bool _scrollTrackingSetup = false;
	int _lastEventCount = 0;
	DotNetObjectReference<WorkingPanel>? _dotNetRef;

	protected override void OnParametersSet()
	{
		// Start timer when thinking starts
		if(IsVisible && Group is not null && Group.Status == GroupStatus.Running)
		{
			_timer ??= new Timer(_ =>
			{
				// Only scroll if there's new content
				int currentEventCount = Group?.GetEventsSnapshot()?.Count ?? 0;
				if(currentEventCount > _lastEventCount)
				{
					_shouldScrollToBottom = true;
					_lastEventCount = currentEventCount;
				}
				InvokeAsync(StateHasChanged);
			}, null, 0, 200); // Check every 200ms for faster response
		}
		else
		{
			// Stop timer when thinking completes
			_timer?.Dispose();
			_timer = null;
		}

		// Reset scroll tracking when panel becomes invisible
		if(!IsVisible && _scrollTrackingSetup)
		{
			_scrollTrackingSetup = false;
		}
	}

	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		if(firstRender)
		{
			_dotNetRef = DotNetObjectReference.Create(this);
		}

		// Setup scroll tracking when panel becomes visible (element is in DOM)
		if(IsVisible && !_scrollTrackingSetup && _dotNetRef is not null)
		{
			_scrollTrackingSetup = true;
			_lastEventCount = Group?.GetEventsSnapshot()?.Count ?? 0; // Initialize count
			await SetupSmartScroll();
		}

		if(_shouldScrollToBottom && !_isUserScrolledUpFromWorking)
		{
			_shouldScrollToBottom = false;
			await ScrollToBottom();
		}
	}

	async Task SetupSmartScroll()
	{
		try
		{
			await JSRuntime.InvokeVoidAsync("cockpit.setupSmartScroll", "workingContent", _dotNetRef, "OnWorkingPanelScrollPositionChanged");
		}
		catch(Exception ex)
		{
			Logger.LogDebug(ex, "Failed to setup smart scroll for working panel");
		}
	}

	[JSInvokable]
	public void OnWorkingPanelScrollPositionChanged(bool isNearBottom)
	{
		_isUserScrolledUpFromWorking = !isNearBottom;
	}

	async Task ScrollToBottom()
	{
		try
		{
			await JSRuntime.InvokeVoidAsync("cockpit.scrollToBottom", "workingContent");
		}
		catch(Exception ex)
		{
			Logger.LogDebug(ex, "Failed to scroll working panel to bottom");
		}
	}

	public async ValueTask DisposeAsync()
	{
		_timer?.Dispose();

		if(_scrollTrackingSetup)
		{
			try
			{
				await JSRuntime.InvokeVoidAsync("cockpit.cleanupSmartScroll", "workingContent");
			}
			catch(Exception ex)
			{
				Logger.LogDebug(ex, "Failed to cleanup smart scroll for working panel");
			}
		}

		_dotNetRef?.Dispose();
	}

	string GetElapsedTime()
	{
		if(Group is null)
		{
			return string.Empty;
		}

		TimeSpan elapsed = Group.Status == GroupStatus.Running
			? DateTime.Now - Group.StartTime
			: (Group.EndTime ?? DateTime.Now) - Group.StartTime;

		return elapsed.Humanize();
	}

	string GenerateSummary()
	{
		if(Group is null)
		{
			return string.Empty;
		}

		List<ToolExecution> tools = [.. Group.Tools]; // Create snapshot to avoid collection modified exception
		int running = tools.Count(t => t.Status == ToolStatus.Running);
		int complete = tools.Count(t => t.Status == ToolStatus.Success);

		return $"{running} running, {complete} complete";
	}

	async Task StopSession()
	{
		await SessionManager.AbortCurrentSessionAsync();
	}
}