using System.Text.Json;
using Cockpit.Models;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Controls;

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
		if(Tool.InputParameters is null)
		{
			return string.Empty;
		}

		try
		{
			switch(Tool.ToolName)
			{
				case "report_intent":
					return GetValue(Tool.InputParameters, "intent") ?? string.Empty;
				case "view":
					string? viewPath = GetValue(Tool.InputParameters, "path");
					if(string.IsNullOrEmpty(viewPath))
					{
						return string.Empty;
					}

					string viewFileName = viewPath.Contains('/') || viewPath.Contains('\\')
						? Path.GetFileName(viewPath)
						: viewPath;

					if(Tool.InputParameters.TryGetValue("view_range", out object? rangeObj))
					{
						return $"{viewFileName} lines {rangeObj}";
					}

					return viewFileName;
				case "edit":
					string? editPath = GetValue(Tool.InputParameters, "path");
					if(string.IsNullOrEmpty(editPath))
					{
						return string.Empty;
					}

					string editFileName = editPath.Contains('/') || editPath.Contains('\\')
						? Path.GetFileName(editPath)
						: editPath;

					string? oldStr = GetValue(Tool.InputParameters, "old_str");
					string? newStr = GetValue(Tool.InputParameters, "new_str");

					if(!string.IsNullOrEmpty(oldStr) || !string.IsNullOrEmpty(newStr))
					{
						int added = newStr?.Split('\n').Length ?? 0;
						int removed = oldStr?.Split('\n').Length ?? 0;
						return $"{editFileName} (+{added} -{removed})";
					}

					return editFileName;
				case "create":
					string? createPath = GetValue(Tool.InputParameters, "path");
					if(string.IsNullOrEmpty(createPath))
					{
						return string.Empty;
					}

					return createPath.Contains('/') || createPath.Contains('\\')
						? Path.GetFileName(createPath)
						: createPath;
				case "bash" or "powershell":
					return GetValue(Tool.InputParameters, "command") ?? string.Empty;
				case "grep":
					string? grepPattern = GetValue(Tool.InputParameters, "pattern");
					string glob = GetValue(Tool.InputParameters, "glob") ?? GetValue(Tool.InputParameters, "path") ?? ".";
					return $"{grepPattern} in {glob}";
				case "glob":
					return GetValue(Tool.InputParameters, "pattern") ?? string.Empty;
				case "web_fetch":
					return GetValue(Tool.InputParameters, "url") ?? string.Empty;
				case "web_search":
					return GetValue(Tool.InputParameters, "query") ?? string.Empty;
				case "sql":
					return GetValue(Tool.InputParameters, "query") ?? string.Empty;
				case "task":
					return GetValue(Tool.InputParameters, "description") ?? string.Empty;
				case "ask_user":
					return GetValue(Tool.InputParameters, "question") ?? string.Empty;
			}

			object? first = Tool.InputParameters.Values.FirstOrDefault();
			if(first is null)
			{
				return string.Empty;
			}

			return first.ToString() ?? string.Empty;
		}
		catch
		{
			return string.Empty;
		}

		static string? GetValue(Dictionary<string, object> dict, string key)
		{
			if(dict.TryGetValue(key, out object? value))
			{
				return value?.ToString();
			}
			return null;
		}
	}

	string GetDuration()
	{
		if(!Tool.EndTime.HasValue)
		{
			return string.Empty;
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