using System.Text.Json;

namespace Cockpit.Utilities;

public static class JsonUtil
{
	static readonly JsonSerializerOptions _indentedOptions = new() { WriteIndented = true };

	public static string FormatJsonList(IEnumerable<string>? jsonList)
	{
		if(jsonList == null)
		{
			return string.Empty;
		}

		List<string> list = [.. jsonList];

		if(list.Count == 0)
		{
			return string.Empty;
		}

		try
		{
			List<JsonElement> parsed = [.. list.Select(json => JsonSerializer.Deserialize<JsonElement>(json))];
			return JsonSerializer.Serialize(parsed, _indentedOptions);
		}
		catch
		{
			return string.Join("\n\n", list);
		}
	}
}
