using Cockpit.Models;
using Cockpit.Services;
using Humanizer;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Cockpit.Components;

public sealed partial class WorkingPanel : IDisposable
{
	[Parameter]
	public ActivityGroup? Group { get; set; }

	[Parameter]
	public bool IsVisible { get; set; }

	[Inject]
	UnifiedSessionManager SessionManager { get; set; } = default!;

	[Inject]
	IJSRuntime JSRuntime { get; set; } = default!;

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
		catch
		{
			// Handle error silently
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
		catch
		{
			// Handle error silently
		}
	}

	public void Dispose()
	{
		_timer?.Dispose();

		if(_scrollTrackingSetup)
		{
			try
			{
				JSRuntime.InvokeVoidAsync("cockpit.cleanupSmartScroll", "workingContent");
			}
			catch
			{
				// Handle error silently
			}
		}

		_dotNetRef?.Dispose();
		GC.SuppressFinalize(this);
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