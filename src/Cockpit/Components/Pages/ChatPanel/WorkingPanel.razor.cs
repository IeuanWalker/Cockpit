using Cockpit.Features.SessionEvents.Models;
using Cockpit.Features.Sessions;
using Cockpit.Features.Timestamp;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Cockpit.Components.Pages.ChatPanel;

public sealed partial class WorkingPanel : IAsyncDisposable
{
	[Parameter] public ActivityGroupModel? Group { get; set; }
	[Parameter] public bool IsVisible { get; set; }

	readonly SessionFeature _sessionFeature;
	readonly IJSRuntime _jsRuntime;
	readonly ILogger<WorkingPanel> _logger;
	readonly ITimestampFeature _timestampFeature;

	public WorkingPanel(
		SessionFeature sessionFeature,
		IJSRuntime jsRuntime,
		ILogger<WorkingPanel> logger,
		ITimestampFeature timestampFeature)
	{
		_sessionFeature = sessionFeature;
		_jsRuntime = jsRuntime;
		_logger = logger;
		_timestampFeature = timestampFeature;
	}

	Timer? _timer;
	bool _isUserScrolledUpFromWorking = false;
	bool _scrollTrackingSetup = false;
	bool _pendingScrollToBottom = false;
	DotNetObjectReference<WorkingPanel>? _dotNetRef;

	string? _prevSessionId;
	string? _prevWorkingGroupId;

	protected override void OnParametersSet()
	{
		string? currentSessionId = _sessionFeature.CurrentSession?.Id;
		string? currentWorkingGroupId = Group?.Id;

		// Only reset scroll state if session or working group actually changed
		if((currentSessionId != _prevSessionId || currentWorkingGroupId != _prevWorkingGroupId) && IsVisible && Group is not null && Group.Status == GroupStatusEnum.Running)
		{
			_isUserScrolledUpFromWorking = false; // Re-enable auto-scroll
			_pendingScrollToBottom = true;
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

		// Setup scroll tracking when the panel becomes visible (but not on every subsequent render —
		// re-registering on every render caused a render loop because the new JS implementation
		// immediately fires the scroll-position callback on each registration).
		// Only mark as set up if the element was found — the element is conditionally rendered
		// and may not exist yet on the first render (when events are still empty). When that
		// happens, leave _scrollTrackingSetup = false so the next render retries.
		if(IsVisible && _dotNetRef is not null && !_scrollTrackingSetup)
		{
			_scrollTrackingSetup = await SetupSmartScroll();
			if(_scrollTrackingSetup)
			{
				// Ensure we start at bottom after the first successful setup, because the
				// pending scroll-to-bottom from OnParametersSet may have been a no-op if
				// the element didn't exist yet on that render.
				_pendingScrollToBottom = true;
			}
		}

		if(_pendingScrollToBottom && IsVisible)
		{
			_pendingScrollToBottom = false;
			await ScrollToBottom();
		}
	}

	async Task<bool> SetupSmartScroll()
	{
		try
		{
			return await _jsRuntime.InvokeAsync<bool>("cockpit.setupSmartScroll", "workingContent", _dotNetRef, "OnWorkingPanelScrollPositionChanged", nameof(WorkingPanel));
		}
		catch(Exception ex)
		{
			_logger.LogDebug(ex, "Failed to setup smart scroll for working panel");
			return false;
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
			await _jsRuntime.InvokeVoidAsync("cockpit.cleanupSmartScroll", "workingContent", nameof(WorkingPanel));
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

		DateTime? end = Group.Status == GroupStatusEnum.Running ? null : Group.EndTime;
		return _timestampFeature.FormatDuration(Group.StartTime, end);
	}

	string GenerateSummary()
	{
		if(Group is null)
		{
			return string.Empty;
		}

		List<ToolExecutionModel> allTools = GetAllTools([.. Group.Tools]);
		int running = allTools.Count(t => t.Status == ToolStatusEnum.Running);
		int complete = allTools.Count(t => t.Status == ToolStatusEnum.Success);

		return $"{running} running, {complete} complete";
	}

	static List<ToolExecutionModel> GetAllTools(List<ToolExecutionModel> tools)
	{
		List<ToolExecutionModel> result = [];
		foreach(ToolExecutionModel tool in tools)
		{
			result.Add(tool);
			result.AddRange(GetAllTools(tool.GetChildrenSnapshot()));
		}
		return result;
	}

	async Task ScrollToBottomAndResume()
	{
		_isUserScrolledUpFromWorking = false;
		await ScrollToBottom();
	}

	async Task StopSession()
	{
		if(_sessionFeature.CurrentSession?.Id is null)
		{
			return;
		}

		await _sessionFeature.AbortSession(_sessionFeature.CurrentSession.Id);
	}

	readonly Dictionary<string, bool> _expandedEventJson = [];
	bool _isSelectingThinking = false;

	void OnThinkingMouseDown(MouseEventArgs e) => _isSelectingThinking = false;
	void OnThinkingMouseMove(MouseEventArgs e)
	{
		if(e.Buttons == 1)
		{
			_isSelectingThinking = true;
		}
	}

	void ToggleEventExpandedIfNotSelecting(string key)
	{
		if(_isSelectingThinking)
		{
			_isSelectingThinking = false;
			return;
		}
		_expandedEventJson[key] = !_expandedEventJson.GetValueOrDefault(key);
		StateHasChanged();
	}

	bool IsEventExpanded(string key) => _expandedEventJson.GetValueOrDefault(key);
}