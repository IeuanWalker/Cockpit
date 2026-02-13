namespace Cockpit.Services;

public class ActivityGroupingService
{
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
