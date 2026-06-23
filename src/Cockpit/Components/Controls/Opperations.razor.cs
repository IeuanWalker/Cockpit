using Cockpit.Features.SessionEvents.Models;
using Cockpit.Features.Timestamp;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Cockpit.Components.Controls;

public sealed partial class Opperations : IDisposable
{
	[Parameter] public ActivityGroupModel Group { get; set; } = default!;

	readonly ITimestampFeature _timestampFeature;

	public Opperations(ITimestampFeature timestampFeature)
	{
		_timestampFeature = timestampFeature;
	}

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

		CancellationTokenSource? oldCts = _clickCts;
		_clickCts = null;
		oldCts?.Cancel();
		oldCts?.Dispose();

		if(elapsed < ThresholdMs)
		{
			return;
		}

		CancellationTokenSource cts = new();
		_clickCts = cts;

		try
		{
			await Task.Delay(ThresholdMs, cts.Token);
			Group.IsExpanded = !Group.IsExpanded;
			StateHasChanged();
		}
		catch(OperationCanceledException) { }
		finally
		{
			if(ReferenceEquals(_clickCts, cts))
			{
				_clickCts = null;
			}

			cts.Dispose();
		}
	}

	public void Dispose()
	{
		_clickCts?.Cancel();
		_clickCts?.Dispose();
		_clickCts = null;
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

	string GetStatusIcon() => Group.Status switch
	{
		GroupStatusEnum.Running => "○",
		GroupStatusEnum.Error => "✗",
		_ => "✓"
	};

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
		List<string> distinctNames = [.. tools.Select(t => t.ToolName).Distinct()];
		string preview = string.Join(", ", distinctNames.Take(3)) + (distinctNames.Count > 3 ? $", +{distinctNames.Count - 3}" : string.Empty);

		string result = $"{tools.Sum(t => CountAllTools(t))} operations ({preview})";

		static int CountAllTools(ToolExecutionModel tool)
		{
			List<ToolExecutionModel> children = tool.GetChildrenSnapshot();
			return 1 + children.Sum(CountAllTools);
		}

		TimeSpan? elapsedTime = Group.EndTime - Group.StartTime;

		if(elapsedTime.HasValue)
		{
			result += $" - {_timestampFeature.FormatDuration(Group.StartTime, Group.EndTime)}";
		}

		return result;
	}
}