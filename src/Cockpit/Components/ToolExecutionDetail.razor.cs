using System.Text.Json;
using Cockpit.Models;
using Cockpit.Services;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components;

public partial class ToolExecutionDetail
{
	[Parameter]
	public ToolExecution Tool { get; set; } = default!;

	[Parameter]
	public bool IsLive { get; set; }

	void ToggleExpanded()
	{
		Tool.IsExpanded = !Tool.IsExpanded;
		StateHasChanged();
	}

	string GetStatusClass()
	{
		return Tool.Status switch
		{
			ToolStatus.Running => "running",
			ToolStatus.Success => "success",
			ToolStatus.Error => "error",
			_ => ""
		};
	}

	string GetToolLabel()
	{
		(string? label, string _) = ActivityGroupingService.GetToolLabel(Tool.ToolName ?? "unknown");
		return label;
	}

	string GetLabelColor()
	{
		(string _, string? color) = ActivityGroupingService.GetToolLabel(Tool.ToolName ?? "unknown");
		return color;
	}

	string GetToolDescription()
	{
		return ActivityGroupingService.GenerateDescription(Tool.ToolName, Tool.InputParameters);
	}

	string GetDuration()
	{
		if(!Tool.EndTime.HasValue)
		{
			return "";
		}

		TimeSpan duration = Tool.EndTime.Value - Tool.StartTime;
		if(duration.TotalSeconds < 1)
		{
			return "<1s";
		}

		if(duration.TotalSeconds < 60)
		{
			return $"{duration.TotalSeconds:F1}s";
		}

		return $"{duration.TotalMinutes:F1}m";
	}

	string SerializeJson(object obj)
	{
		try
		{
			return JsonSerializer.Serialize(obj, new JsonSerializerOptions
			{
				WriteIndented = true
			});
		}
		catch
		{
			return obj?.ToString() ?? "";
		}
	}
}