using System.Text.Json;
using GitHub.Copilot;

namespace Cockpit.Features.ElicitationRequests;

public enum ElicitationFieldType
{
	String,
	Boolean,
	Number,
	Integer,
	EnumString,
	Unsupported,
}

/// <summary>
/// A strongly-typed representation of one property entry in an <see cref="ElicitationSchema"/>.
/// Parsed eagerly from <c>JsonElement</c> so the UI layer never touches raw JSON.
/// </summary>
public class ElicitationSchemaField
{
	public required string FieldName { get; init; }
	public required ElicitationFieldType Type { get; init; }
	public string? Title { get; init; }
	public string? Description { get; init; }
	public List<string> EnumValues { get; init; } = [];
	public string? Default { get; init; }
	public int? MinLength { get; init; }
	public int? MaxLength { get; init; }
	public bool IsRequired { get; init; }

	/// <summary>
	/// Parses all properties in <paramref name="schema"/> into typed <see cref="ElicitationSchemaField"/> instances.
	/// </summary>
	public static ElicitationSchemaField[] ParseFrom(ElicitationSchema schema)
	{
		if(schema.Properties is null || schema.Properties.Count == 0)
		{
			return [];
		}

		IList<string> required = schema.Required ?? [];
		List<ElicitationSchemaField> result = [];

		foreach(KeyValuePair<string, object> entry in schema.Properties)
		{
			bool isRequired = required.Contains(entry.Key);

			if(entry.Value is not JsonElement element)
			{
				result.Add(new ElicitationSchemaField { FieldName = entry.Key, Type = ElicitationFieldType.Unsupported, IsRequired = isRequired });
				continue;
			}

			result.Add(ParseField(entry.Key, element, isRequired));
		}

		return [.. result];
	}

	static ElicitationSchemaField ParseField(string name, JsonElement element, bool isRequired)
	{
		string? typeStr = element.TryGetProperty("type", out JsonElement typeProp) ? typeProp.GetString() : null;
		string? title = element.TryGetProperty("title", out JsonElement titleProp) ? titleProp.GetString() : null;
		string? description = element.TryGetProperty("description", out JsonElement descProp) ? descProp.GetString() : null;

		string? defaultValue = null;
		if(element.TryGetProperty("default", out JsonElement defaultProp))
		{
			defaultValue = defaultProp.ValueKind switch
			{
				JsonValueKind.String => defaultProp.GetString(),
				JsonValueKind.True => "true",
				JsonValueKind.False => "false",
				_ => defaultProp.ToString()
			};
		}

		List<string> enumValues = [];
		if(element.TryGetProperty("enum", out JsonElement enumProp) && enumProp.ValueKind == JsonValueKind.Array)
		{
			foreach(JsonElement v in enumProp.EnumerateArray())
			{
				enumValues.Add(v.ValueKind == JsonValueKind.String ? v.GetString()! : v.ToString());
			}
		}

		ElicitationFieldType fieldType = (typeStr, enumValues.Count > 0) switch
		{
			("string", true) => ElicitationFieldType.EnumString,
			("string", _) => ElicitationFieldType.String,
			("boolean", _) => ElicitationFieldType.Boolean,
			("number", _) => ElicitationFieldType.Number,
			("integer", _) => ElicitationFieldType.Integer,
			_ => ElicitationFieldType.Unsupported,
		};

		int? minLength = null;
		if(element.TryGetProperty("minLength", out JsonElement minProp) && minProp.TryGetInt32(out int min))
		{
			minLength = min;
		}

		int? maxLength = null;
		if(element.TryGetProperty("maxLength", out JsonElement maxProp) && maxProp.TryGetInt32(out int max))
		{
			maxLength = max;
		}

		return new ElicitationSchemaField
		{
			FieldName = name,
			Type = fieldType,
			Title = title,
			Description = description,
			EnumValues = enumValues,
			Default = defaultValue,
			MinLength = minLength,
			MaxLength = maxLength,
			IsRequired = isRequired,
		};
	}
}
