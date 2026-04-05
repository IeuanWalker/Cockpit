using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cockpit.Extensions;

public static class JsonExtensions
{
	static readonly JsonSerializerOptions defaultSerializerSettings = new()
	{
		PropertyNameCaseInsensitive = true,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		WriteIndented = true
	};

	public static T? DeserializeJson<T>(this string? json)
	{
		if(string.IsNullOrWhiteSpace(json))
		{
			return default;
		}
		return JsonSerializer.Deserialize<T>(json, defaultSerializerSettings);
	}

	public static T? DeserializeJson<T>(this string? json, JsonSerializerOptions? options)
	{
		if(string.IsNullOrWhiteSpace(json))
		{
			return default;
		}
		return JsonSerializer.Deserialize<T>(json, options);
	}

	public static string? SerializeJson<T>(this T? data)
	{
		if(data is null)
		{
			return null;
		}

		return JsonSerializer.Serialize(data, defaultSerializerSettings);
	}

	public static string? SerializeJson<T>(this T? data, JsonSerializerOptions? options)
	{
		if(data is null)
		{
			return null;
		}

		return JsonSerializer.Serialize(data, options);
	}
}