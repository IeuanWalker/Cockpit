using Cockpit.Features.SessionEvents.Models;
using Humanizer;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Cockpit.Components.Controls;

public sealed partial class Opperations : IDisposable
{
	[Parameter] public ActivityGroupModel Group { get; set; } = default!;

	bool _isSelectingThinking = false;

	long _lastClickTick = 0;
	CancellationTokenSource? _clickCts;

	void OnThinkingMouseDown(MouseEventArgs e) => _isSelectingThinking = false;
	void OnThinkingMouseMove(MouseEventArgs e)
	{
		if(e.Buttons == 1)
		{
			_isSelectingThinking = true;
		}
	}

	async Task ToggleExpanded()
	{
		const int ThresholdMs = 350;

		long now = Environment.TickCount64;
		long elapsed = now - _lastClickTick;
		_lastClickTick = now;

		_clickCts?.Cancel();
		CancellationTokenSource cts = new();
		_clickCts = cts;

		if (elapsed < ThresholdMs)
			return;

		try
		{
			await Task.Delay(ThresholdMs, cts.Token);
			Group.IsExpanded = !Group.IsExpanded;
			StateHasChanged();
		}
		catch (OperationCanceledException) { }
		finally
		{
			if (ReferenceEquals(_clickCts, cts))
				_clickCts = null;
		}
	}

	public void Dispose()
	{
		_clickCts?.Cancel();
	}

	string GetStatusClass()
	{
		return Group.Status switch
		{
			GroupStatusEnum.Running => "running",
			GroupStatusEnum.Complete => "complete",
			GroupStatusEnum.Error => "error",
			_ => string.Empty
		};
	}

	string GetStatusIcon()
	{
		List<ToolExecutionModel> tools = [.. Group.Tools]; // Create snapshot
		bool hasRunning = tools.Any(t => t.Status == ToolStatusEnum.Running);
		bool hasError = tools.Any(t => t.Status == ToolStatusEnum.Error);

		if(hasRunning)
		{
			return "○";
		}

		if(hasError)
		{
			return "✗";
		}

		return "✓";
	}

	readonly Dictionary<string, bool> _expandedEventJson = [];

	void ToggleEventExpandedIfNotSelecting(string key)
	{
		if(_isSelectingThinking)
		{
			_isSelectingThinking = false;
			return;
		}

		ToggleEventExpanded(key);
	}

	void ToggleEventExpanded(string key)
	{
		_expandedEventJson[key] = !_expandedEventJson.GetValueOrDefault(key);
		StateHasChanged();
	}

	bool IsEventExpanded(string key) => _expandedEventJson.GetValueOrDefault(key);

	string GenerateSummary()
	{
		List<ToolExecutionModel> tools = [.. Group.Tools]; // Create snapshot
		int running = tools.Count(t => t.Status == ToolStatusEnum.Running);
		int complete = tools.Count(t => t.Status == ToolStatusEnum.Success);

		if(running > 0)
		{
			return $"{running} running, {complete} done";
		}

		// Get unique tool names
		IEnumerable<string> toolNames = tools.Select(t => t.ToolName).Distinct().Take(3);
		int more = tools.Select(t => t.ToolName).Distinct().Count() - 3;
		string preview = string.Join(", ", toolNames) + (more > 0 ? $", +{more}" : string.Empty);

		string result = $"{tools.Sum(t => CountAllTools(t))} operations ({preview})";

		static int CountAllTools(ToolExecutionModel tool)
		{
			List<ToolExecutionModel> children = tool.GetChildrenSnapshot();
			return 1 + children.Sum(CountAllTools);
		}

		TimeSpan? elapsedTime = Group.EndTime - Group.StartTime;

		if(elapsedTime.HasValue)
		{
			result += $" - {elapsedTime.Value.Humanize()}";
		}

		return result;
	}
}