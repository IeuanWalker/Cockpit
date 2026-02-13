using Cockpit.Models;
using Humanizer;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Controls;

public partial class Opperations
{
	[Parameter]
	public ActivityGroup Group { get; set; } = default!;

	void ToggleExpanded()
	{
		Group.IsExpanded = !Group.IsExpanded;
		StateHasChanged();
	}

	string GetStatusClass()
	{
		return Group.Status switch
		{
			GroupStatus.Running => "running",
			GroupStatus.Complete => "complete",
			GroupStatus.Error => "error",
			_ => string.Empty
		};
	}

	string GetStatusIcon()
	{
		List<ToolExecution> tools = [.. Group.Tools]; // Create snapshot
		bool hasRunning = tools.Any(t => t.Status == ToolStatus.Running);
		bool hasError = tools.Any(t => t.Status == ToolStatus.Error);

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

	string GenerateSummary()
	{
		List<ToolExecution> tools = [.. Group.Tools]; // Create snapshot
		int running = tools.Count(t => t.Status == ToolStatus.Running);
		int complete = tools.Count(t => t.Status == ToolStatus.Success);

		if(running > 0)
		{
			return $"{running} running, {complete} done";
		}

		// Get unique tool names
		IEnumerable<string> toolNames = tools.Select(t => t.ToolName).Distinct().Take(3);
		int more = tools.Select(t => t.ToolName).Distinct().Count() - 3;
		string preview = string.Join(", ", toolNames) + (more > 0 ? $", +{more}" : string.Empty);

		string result = $"{tools.Count} operations ({preview})";

		TimeSpan? elapsedTime = Group.EndTime - Group.StartTime;

		if(elapsedTime.HasValue)
		{
			result += $" - {elapsedTime.Value.Humanize()}";
		}

		return result;
	}
}