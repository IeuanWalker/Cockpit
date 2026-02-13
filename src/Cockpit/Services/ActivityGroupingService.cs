namespace Cockpit.Services;

public class ActivityGroupingService
{
	// PolyPilot-style description - shows key info even when collapsed
	public static string GenerateDescription(string toolName, Dictionary<string, object>? inputParams)
	{
		if(inputParams is null)
		{
			return string.Empty;
		}

		try
		{
			return toolName switch
			{
				"report_intent" => FormatReportIntentDescription(inputParams),
				"view" => FormatViewDescription(inputParams),
				"edit" => FormatEditDescription(inputParams),
				"create" => FormatCreateDescription(inputParams),
				"bash" or "powershell" => FormatBashDescription(inputParams),
				"grep" => FormatGrepDescription(inputParams),
				"glob" => FormatGlobDescription(inputParams),
				"web_fetch" => FormatWebFetchDescription(inputParams),
				"web_search" => FormatWebSearchDescription(inputParams),
				"sql" => FormatSqlDescription(inputParams),
				"task" => FormatTaskDescription(inputParams),
				"ask_user" => FormatAskUserDescription(inputParams),
				_ => GetFirstParamValue(inputParams)
			};
		}
		catch
		{
			return string.Empty;
		}
	}
	static string FormatReportIntentDescription(Dictionary<string, object> inputParams)
	{
		if(inputParams.TryGetValue("intent", out object? value))
		{
			return value?.ToString() ?? string.Empty;
		}

		return string.Empty;
	}

	static string FormatViewDescription(Dictionary<string, object> inputParams)
	{
		string? path = GetValue(inputParams, "path");
		if(string.IsNullOrEmpty(path))
		{
			return string.Empty;
		}

		string fileName = path.Contains('/') || path.Contains('\\')
			? Path.GetFileName(path)
			: path;

		if(inputParams.TryGetValue("view_range", out object? rangeObj))
		{
			return $"{fileName} lines {rangeObj}";
		}

		return fileName;
	}

	static string FormatEditDescription(Dictionary<string, object> inputParams)
	{
		string? path = GetValue(inputParams, "path");
		if(string.IsNullOrEmpty(path))
		{
			return "";
		}

		string fileName = path.Contains('/') || path.Contains('\\')
			? Path.GetFileName(path)
			: path;

		string? oldStr = GetValue(inputParams, "old_str");
		string? newStr = GetValue(inputParams, "new_str");

		if(!string.IsNullOrEmpty(oldStr) || !string.IsNullOrEmpty(newStr))
		{
			int added = newStr?.Split('\n').Length ?? 0;
			int removed = oldStr?.Split('\n').Length ?? 0;
			return $"{fileName} (+{added} -{removed})";
		}

		return fileName;
	}

	static string FormatCreateDescription(Dictionary<string, object> inputParams)
	{
		string? path = GetValue(inputParams, "path");
		if(string.IsNullOrEmpty(path))
		{
			return string.Empty;
		}

		return path.Contains('/') || path.Contains('\\')
			? Path.GetFileName(path)
			: path;
	}

	static string FormatBashDescription(Dictionary<string, object> inputParams)
	{
		string? command = GetValue(inputParams, "command");
		return !string.IsNullOrEmpty(command) ? $"$ {Truncate(command, 80)}" : "";
	}

	static string FormatGrepDescription(Dictionary<string, object> inputParams)
	{
		string? pattern = GetValue(inputParams, "pattern");
		string glob = GetValue(inputParams, "glob") ?? GetValue(inputParams, "path") ?? ".";
		return $"{pattern} in {glob}";
	}

	static string FormatGlobDescription(Dictionary<string, object> inputParams)
	{
		string? pattern = GetValue(inputParams, "pattern");
		return pattern ?? string.Empty;
	}

	static string FormatWebFetchDescription(Dictionary<string, object> inputParams)
	{
		string? url = GetValue(inputParams, "url");
		return Truncate(url ?? string.Empty, 80);
	}

	static string FormatWebSearchDescription(Dictionary<string, object> inputParams)
	{
		string? query = GetValue(inputParams, "query");
		return Truncate(query ?? string.Empty, 80);
	}

	static string FormatSqlDescription(Dictionary<string, object> inputParams)
	{
		string? query = GetValue(inputParams, "query");
		return Truncate(query ?? string.Empty, 80);
	}

	static string FormatTaskDescription(Dictionary<string, object> inputParams)
	{
		string? desc = GetValue(inputParams, "description");
		return Truncate(desc ?? string.Empty, 80);
	}

	static string FormatAskUserDescription(Dictionary<string, object> inputParams)
	{
		string? question = GetValue(inputParams, "question");
		return Truncate(question ?? string.Empty, 80);
	}

	static string? GetValue(Dictionary<string, object> dict, string key)
	{
		if(dict.TryGetValue(key, out object? value))
		{
			return value?.ToString();
		}
		return null;
	}

	static string GetFirstParamValue(Dictionary<string, object> inputParams)
	{
		object? first = inputParams.Values.FirstOrDefault();
		if(first is null)
		{
			return string.Empty;
		}

		string str = first.ToString() ?? string.Empty;
		return Truncate(str, 80);
	}

	static string Truncate(string s, int max)
	{
		if(s.Length <= max)
		{
			return s;
		}

		return s[..max] + "…";
	}

	// Legacy method - kept for compatibility
	public static string GenerateInputSummary(string toolName, Dictionary<string, object>? input)
	{
		return GenerateDescription(toolName, input);
	}

	// Deserialize JsonElement arguments to Dictionary<string, object>
	public static Dictionary<string, object>? DeserializeArguments(object? arguments)
	{
		if(arguments is null)
		{
			return null;
		}

		try
		{
			// If it's already a dictionary, return it
			if(arguments is Dictionary<string, object> dict)
			{
				return dict;
			}

			// If it's a JsonElement, deserialize it
			if(arguments is System.Text.Json.JsonElement je)
			{
				if(je.ValueKind == System.Text.Json.JsonValueKind.Object)
				{
					return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(je.GetRawText());
				}
			}
		}
		catch
		{
			// Fall through to return null
		}

		return null;
	}
}
