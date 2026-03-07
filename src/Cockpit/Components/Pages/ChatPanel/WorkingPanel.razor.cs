using Cockpit.Features.SessionEvents.Models;
using Cockpit.Features.Sessions;
using Humanizer;
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
	public WorkingPanel(
		SessionFeature sessionFeature,
		IJSRuntime jsRuntime,
		ILogger<WorkingPanel> logger)
	{
		_sessionFeature = sessionFeature;
		_jsRuntime = jsRuntime;
		_logger = logger;
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

		// Always re-setup scroll tracking when panel becomes visible or session/working group changes
		if(IsVisible && _dotNetRef is not null)
		{
			await SetupSmartScroll();
			_scrollTrackingSetup = true;
		}

		if(_pendingScrollToBottom && IsVisible)
		{
			_pendingScrollToBottom = false;
			await ScrollToBottom();
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