using System.Text.Json;
using Cockpit.Features.ElicitationRequests;
using GitHub.Copilot;
using Shouldly;

namespace Cockpit.UnitTests.Features.ElicitationRequests;

public sealed class ElicitationSchemaFieldTests
{
	/// <summary>Builds an <see cref="ElicitationSchema"/> with properties described by a JSON object.</summary>
	static ElicitationSchema BuildSchema(string propertiesJson, IList<string>? required = null)
	{
		JsonDocument doc = JsonDocument.Parse(propertiesJson);
		Dictionary<string, object> props = [];
		foreach(JsonProperty prop in doc.RootElement.EnumerateObject())
		{
			props[prop.Name] = prop.Value.Clone();
		}

		return new ElicitationSchema { Properties = props, Required = required ?? [], Type = "object" };
	}

	// ── Empty / null schema ───────────────────────────────────────────────────

	[Fact]
	public void ParseFrom_NullProperties_ReturnsEmpty()
	{
		ElicitationSchema schema = new() { Type = "object" };

		ElicitationSchemaField[] result = ElicitationSchemaField.ParseFrom(schema);

		result.ShouldBeEmpty();
	}

	[Fact]
	public void ParseFrom_EmptyProperties_ReturnsEmpty()
	{
		ElicitationSchema schema = BuildSchema("{}");

		ElicitationSchemaField[] result = ElicitationSchemaField.ParseFrom(schema);

		result.ShouldBeEmpty();
	}

	// ── Field types ───────────────────────────────────────────────────────────

	[Fact]
	public void ParseFrom_StringField_ParsedAsString()
	{
		ElicitationSchema schema = BuildSchema("""{"name": {"type": "string"}}""");

		ElicitationSchemaField[] result = ElicitationSchemaField.ParseFrom(schema);

		result.Length.ShouldBe(1);
		result[0].FieldName.ShouldBe("name");
		result[0].Type.ShouldBe(ElicitationFieldType.String);
	}

	[Fact]
	public void ParseFrom_BooleanField_ParsedAsBoolean()
	{
		ElicitationSchema schema = BuildSchema("""{"enabled": {"type": "boolean"}}""");

		ElicitationSchemaField[] result = ElicitationSchemaField.ParseFrom(schema);

		result.Length.ShouldBe(1);
		result[0].FieldName.ShouldBe("enabled");
		result[0].Type.ShouldBe(ElicitationFieldType.Boolean);
	}

	[Fact]
	public void ParseFrom_NumberField_ParsedAsNumber()
	{
		ElicitationSchema schema = BuildSchema("""{"score": {"type": "number"}}""");

		ElicitationSchemaField[] result = ElicitationSchemaField.ParseFrom(schema);

		result[0].Type.ShouldBe(ElicitationFieldType.Number);
	}

	[Fact]
	public void ParseFrom_IntegerField_ParsedAsInteger()
	{
		ElicitationSchema schema = BuildSchema("""{"count": {"type": "integer"}}""");

		ElicitationSchemaField[] result = ElicitationSchemaField.ParseFrom(schema);

		result[0].Type.ShouldBe(ElicitationFieldType.Integer);
	}

	[Fact]
	public void ParseFrom_StringWithEnum_ParsedAsEnumString()
	{
		ElicitationSchema schema = BuildSchema("""{"colour": {"type": "string", "enum": ["red", "green", "blue"]}}""");

		ElicitationSchemaField[] result = ElicitationSchemaField.ParseFrom(schema);

		result[0].Type.ShouldBe(ElicitationFieldType.EnumString);
		result[0].EnumValues.ShouldBe(["red", "green", "blue"]);
	}

	[Fact]
	public void ParseFrom_UnknownType_ParsedAsUnsupported()
	{
		ElicitationSchema schema = BuildSchema("""{"data": {"type": "object"}}""");

		ElicitationSchemaField[] result = ElicitationSchemaField.ParseFrom(schema);

		result[0].Type.ShouldBe(ElicitationFieldType.Unsupported);
	}

	[Fact]
	public void ParseFrom_NonJsonElementValue_ParsedAsUnsupported()
	{
		// Directly supply a non-JsonElement value to Properties
		ElicitationSchema schema = new()
		{
			Type = "object",
			Properties = new Dictionary<string, object> { ["raw"] = "not a JsonElement" }
		};

		ElicitationSchemaField[] result = ElicitationSchemaField.ParseFrom(schema);

		result.Length.ShouldBe(1);
		result[0].FieldName.ShouldBe("raw");
		result[0].Type.ShouldBe(ElicitationFieldType.Unsupported);
	}

	// ── Required ─────────────────────────────────────────────────────────────

	[Fact]
	public void ParseFrom_RequiredField_IsRequiredTrue()
	{
		ElicitationSchema schema = BuildSchema(
			"""{"name": {"type": "string"}, "age": {"type": "integer"}}""",
			required: ["name"]);

		ElicitationSchemaField[] result = ElicitationSchemaField.ParseFrom(schema);

		result.Single(f => f.FieldName == "name").IsRequired.ShouldBeTrue();
		result.Single(f => f.FieldName == "age").IsRequired.ShouldBeFalse();
	}

	// ── Default values ────────────────────────────────────────────────────────

	[Fact]
	public void ParseFrom_StringDefault_ParsedAsString()
	{
		ElicitationSchema schema = BuildSchema("""{"mode": {"type": "string", "default": "auto"}}""");

		ElicitationSchemaField[] result = ElicitationSchemaField.ParseFrom(schema);

		result[0].Default.ShouldBe("auto");
	}

	[Fact]
	public void ParseFrom_TrueBoolDefault_ParsedAsStringTrue()
	{
		ElicitationSchema schema = BuildSchema("""{"active": {"type": "boolean", "default": true}}""");

		ElicitationSchemaField[] result = ElicitationSchemaField.ParseFrom(schema);

		result[0].Default.ShouldBe("true");
	}

	[Fact]
	public void ParseFrom_FalseBoolDefault_ParsedAsStringFalse()
	{
		ElicitationSchema schema = BuildSchema("""{"active": {"type": "boolean", "default": false}}""");

		ElicitationSchemaField[] result = ElicitationSchemaField.ParseFrom(schema);

		result[0].Default.ShouldBe("false");
	}

	[Fact]
	public void ParseFrom_NoDefault_DefaultIsNull()
	{
		ElicitationSchema schema = BuildSchema("""{"name": {"type": "string"}}""");

		ElicitationSchemaField[] result = ElicitationSchemaField.ParseFrom(schema);

		result[0].Default.ShouldBeNull();
	}

	// ── MinLength / MaxLength ─────────────────────────────────────────────────

	[Fact]
	public void ParseFrom_MinLengthAndMaxLength_ParsedCorrectly()
	{
		ElicitationSchema schema = BuildSchema("""{"bio": {"type": "string", "minLength": 10, "maxLength": 500}}""");

		ElicitationSchemaField[] result = ElicitationSchemaField.ParseFrom(schema);

		result[0].MinLength.ShouldBe(10);
		result[0].MaxLength.ShouldBe(500);
	}

	[Fact]
	public void ParseFrom_NoLengthConstraints_MinMaxAreNull()
	{
		ElicitationSchema schema = BuildSchema("""{"name": {"type": "string"}}""");

		ElicitationSchemaField[] result = ElicitationSchemaField.ParseFrom(schema);

		result[0].MinLength.ShouldBeNull();
		result[0].MaxLength.ShouldBeNull();
	}

	// ── Title / Description ───────────────────────────────────────────────────

	[Fact]
	public void ParseFrom_TitleAndDescription_ParsedCorrectly()
	{
		ElicitationSchema schema = BuildSchema("""{"name": {"type": "string", "title": "Your Name", "description": "Enter your full name"}}""");

		ElicitationSchemaField[] result = ElicitationSchemaField.ParseFrom(schema);

		result[0].Title.ShouldBe("Your Name");
		result[0].Description.ShouldBe("Enter your full name");
	}

	[Fact]
	public void ParseFrom_NoTitleOrDescription_BothAreNull()
	{
		ElicitationSchema schema = BuildSchema("""{"name": {"type": "string"}}""");

		ElicitationSchemaField[] result = ElicitationSchemaField.ParseFrom(schema);

		result[0].Title.ShouldBeNull();
		result[0].Description.ShouldBeNull();
	}

	// ── Multiple fields ───────────────────────────────────────────────────────

	[Fact]
	public void ParseFrom_MultipleFields_AllParsed()
	{
		ElicitationSchema schema = BuildSchema("""
			{
				"name": {"type": "string", "title": "Name"},
				"age": {"type": "integer"},
				"active": {"type": "boolean"},
				"score": {"type": "number"},
				"role": {"type": "string", "enum": ["admin", "user"]},
				"blob": {"type": "array"}
			}
			""",
			required: ["name", "age"]);

		ElicitationSchemaField[] result = ElicitationSchemaField.ParseFrom(schema);

		result.Length.ShouldBe(6);
		result.Single(f => f.FieldName == "name").Type.ShouldBe(ElicitationFieldType.String);
		result.Single(f => f.FieldName == "age").Type.ShouldBe(ElicitationFieldType.Integer);
		result.Single(f => f.FieldName == "active").Type.ShouldBe(ElicitationFieldType.Boolean);
		result.Single(f => f.FieldName == "score").Type.ShouldBe(ElicitationFieldType.Number);
		result.Single(f => f.FieldName == "role").Type.ShouldBe(ElicitationFieldType.EnumString);
		result.Single(f => f.FieldName == "blob").Type.ShouldBe(ElicitationFieldType.Unsupported);
		result.Single(f => f.FieldName == "name").IsRequired.ShouldBeTrue();
		result.Single(f => f.FieldName == "age").IsRequired.ShouldBeTrue();
		result.Single(f => f.FieldName == "active").IsRequired.ShouldBeFalse();
	}
}
