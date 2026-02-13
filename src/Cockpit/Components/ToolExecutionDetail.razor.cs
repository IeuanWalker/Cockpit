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

	readonly Dictionary<string, (string Label, string Color)> _toolInfo = new()
	{
		["edit"] = ("Edit", "#f59e0b"),
		["create"] = ("Create", "#22c55e"),
		["view"] = ("Read", "#4ea8d1"),
		["bash"] = ("Run", "#a78bfa"),
		["powershell"] = ("Run", "#a78bfa"),
		["grep"] = ("Search", "#4ea8d1"),
		["glob"] = ("Search", "#4ea8d1"),
		["web_search"] = ("Search", "#4ea8d1"),
		["web_fetch"] = ("Fetch", "#34d399"),
		["sql"] = ("Query", "#f472b6"),
		["task"] = ("Agent", "#c084fc"),
		["ask_user"] = ("Ask", "#fbbf24"),
		["task_complete"] = ("Done", "#22c55e"),
		["store_memory"] = ("Memory", "#a78bfa"),
		["report_intent"] = ("Intent", "#6b7280")
	};

	string GetToolLabel()
	{
		if(_toolInfo.TryGetValue(Tool.ToolName, out (string Label, string Color) info))
		{
			return info.Label;
		}

		if(string.IsNullOrEmpty(Tool.ToolName))
		{
			return string.Empty;
		}

		return string.Join(" ", Tool.ToolName.Split('_').Select(w =>
			w.Length > 0 ? char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant() : w));
	}

	string GetLabelColor()
	{
		if(_toolInfo.TryGetValue(Tool.ToolName, out (string Label, string Color) info))
		{
			return info.Color;
		}

		return "#9ca3af";
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