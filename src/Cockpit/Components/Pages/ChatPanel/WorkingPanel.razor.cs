using Cockpit.Features.SessionEvents.Models;
using Cockpit.Features.Sessions;
using Humanizer;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Cockpit.Components.Pages.ChatPanel;

public sealed partial class WorkingPanel : IAsyncDisposable
{
	[Parameter]
	public ActivityGroupModel? Group { get; set; }

	[Parameter]
	public bool IsVisible { get; set; }

	[Inject] SessionFeature _sessionManager { get; set; } = default!;

	[Inject] IJSRuntime _jsRuntime { get; set; } = default!;

	[Inject] ILogger<WorkingPanel> _logger { get; set; } = default!;

	Timer? _timer;
	bool _isUserScrolledUpFromWorking = false;
	bool _scrollTrackingSetup = false;
	DotNetObjectReference<WorkingPanel>? _dotNetRef;

	string? _prevSessionId;
	string? _prevWorkingGroupId;

	protected override void OnParametersSet()
	{
		string? currentSessionId = _sessionManager.CurrentSession?.Id;
		string? currentWorkingGroupId = Group?.Id;

		// Only reset scroll state if session or working group actually changed
		if((currentSessionId != _prevSessionId || currentWorkingGroupId != _prevWorkingGroupId) && IsVisible && Group is not null && Group.Status == GroupStatusEnum.Running)
		{
			_isUserScrolledUpFromWorking = false; // Re-enable auto-scroll
			_prevSessionId = currentSessionId;
			_prevWorkingGroupId = currentWorkingGroupId;
		}

		if(IsVisible && Group is not null && Group.Status == GroupStatusEnum.Running)
		{
			_timer ??= new Timer(_ => InvokeAsync(StateHasChanged), null, 0, 200);
		}
		else
		{
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

		// Cleanup smart scroll when panel becomes invisible (element leaving DOM)
		if(!IsVisible && _scrollTrackingSetup)
		{
			_scrollTrackingSetup = false;
			await CleanupSmartScroll();
			return;
		}

		// Always re-setup scroll tracking when panel becomes visible or session/working group changes
		if(IsVisible && _dotNetRef is not null)
		{
			await SetupSmartScroll();
			_scrollTrackingSetup = true;
		}
	}

	async Task SetupSmartScroll()
	{
		try
		{
			await _jsRuntime.InvokeVoidAsync("cockpit.setupSmartScroll", "workingContent", _dotNetRef, "OnWorkingPanelScrollPositionChanged");
		}
		catch(Exception ex)
		{
			_logger.LogDebug(ex, "Failed to setup smart scroll for working panel");
		}
	}

	[JSInvokable]
	public void OnWorkingPanelScrollPositionChanged(bool isNearBottom)
	{
		_isUserScrolledUpFromWorking = !isNearBottom;
		InvokeAsync(StateHasChanged);
	}

	async Task ScrollToBottom()
	{
		try
		{
			await _jsRuntime.InvokeVoidAsync("cockpit.scrollToBottom", "workingContent");
		}
		catch(Exception ex)
		{
			_logger.LogDebug(ex, "Failed to scroll working panel to bottom");
		}
	}

	async Task CleanupSmartScroll()
	{
		try
		{
			await _jsRuntime.InvokeVoidAsync("cockpit.cleanupSmartScroll", "workingContent");
		}
		catch(Exception ex)
		{
			_logger.LogDebug(ex, "Failed to cleanup smart scroll for working panel");
		}
	}

	public async ValueTask DisposeAsync()
	{
		_timer?.Dispose();

		if(_scrollTrackingSetup)
		{
			await CleanupSmartScroll();
		}

		_dotNetRef?.Dispose();
	}

	string GetElapsedTime()
	{
		if(Group is null)
		{
			return string.Empty;
		}

		TimeSpan elapsed = Group.Status == GroupStatusEnum.Running
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

		List<ToolExecutionModel> tools = [.. Group.Tools]; // Create snapshot to avoid collection modified exception
		int running = tools.Count(t => t.Status == ToolStatusEnum.Running);
		int complete = tools.Count(t => t.Status == ToolStatusEnum.Success);

		return $"{running} running, {complete} complete";
	}

	async Task ScrollToBottomAndResume()
	{
		_isUserScrolledUpFromWorking = false;
		await ScrollToBottom();
	}

	async Task StopSession()
	{
		if(_sessionManager.CurrentSession?.Id is null)
		{
			return;
		}

		await _sessionManager.AbortSession(_sessionManager.CurrentSession.Id);
	}
}