using GitHub.Copilot;

namespace Cockpit.Features.ElicitationRequests;

public sealed partial class ElicitationFeature
{
	/// <summary>
	/// Simulate a simple form request with two required text fields.
	/// </summary>
	public Task<ElicitationResult> SimulateSimpleFormRequest(string sessionId)
	{
		ElicitationRequestModel request = new()
		{
			SessionId = sessionId,
			Message = "Please provide your contact details to continue.",
			ElicitationSource = "debug-simulator",
			Fields =
			[
				new ElicitationSchemaField
				{
					FieldName = "name",
					Type = ElicitationFieldType.String,
					Title = "Full Name",
					Description = "Your full name",
					IsRequired = true,
				},
				new ElicitationSchemaField
				{
					FieldName = "email",
					Type = ElicitationFieldType.String,
					Title = "Email Address",
					Description = "Your email address",
					IsRequired = true,
				},
			],
		};
		return RequestElicitationAsync(request);
	}

	/// <summary>
	/// Simulate a form with an enum dropdown and a boolean checkbox.
	/// </summary>
	public Task<ElicitationResult> SimulateEnumAndBoolRequest(string sessionId)
	{
		ElicitationRequestModel request = new()
		{
			SessionId = sessionId,
			Message = "Please configure your preferences.",
			ElicitationSource = "debug-simulator",
			Fields =
			[
				new ElicitationSchemaField
				{
					FieldName = "environment",
					Type = ElicitationFieldType.EnumString,
					Title = "Target Environment",
					Description = "The environment to deploy to",
					EnumValues = ["development", "staging", "production"],
					Default = "development",
					IsRequired = true,
				},
				new ElicitationSchemaField
				{
					FieldName = "confirmDeploy",
					Type = ElicitationFieldType.Boolean,
					Title = "Confirm Deployment",
					Description = "I confirm I want to deploy to the selected environment",
					Default = "false",
					IsRequired = true,
				},
			],
		};
		return RequestElicitationAsync(request);
	}

	/// <summary>
	/// Simulate a full form request covering all field types (string, bool, integer, number, enum).
	/// </summary>
	public Task<ElicitationResult> SimulateFullFormRequest(string sessionId)
	{
		ElicitationRequestModel request = new()
		{
			SessionId = sessionId,
			Message = "Fill in all required fields to configure the operation.",
			ElicitationSource = "debug-simulator",
			Fields =
			[
				new ElicitationSchemaField
				{
					FieldName = "projectName",
					Type = ElicitationFieldType.String,
					Title = "Project Name",
					Description = "Name of the project (3–50 characters)",
					MinLength = 3,
					MaxLength = 50,
					IsRequired = true,
				},
				new ElicitationSchemaField
				{
					FieldName = "enabled",
					Type = ElicitationFieldType.Boolean,
					Title = "Enable Feature",
					Description = "Toggle this feature on or off",
					Default = "true",
					IsRequired = false,
				},
				new ElicitationSchemaField
				{
					FieldName = "maxRetries",
					Type = ElicitationFieldType.Integer,
					Title = "Max Retries",
					Description = "Number of retry attempts (0–10)",
					Default = "3",
					IsRequired = false,
				},
				new ElicitationSchemaField
				{
					FieldName = "timeout",
					Type = ElicitationFieldType.Number,
					Title = "Timeout (seconds)",
					Description = "Request timeout in seconds",
					Default = "30.0",
					IsRequired = false,
				},
				new ElicitationSchemaField
				{
					FieldName = "logLevel",
					Type = ElicitationFieldType.EnumString,
					Title = "Log Level",
					Description = "Verbosity of logging",
					EnumValues = ["debug", "info", "warn", "error"],
					Default = "info",
					IsRequired = true,
				},
			],
		};
		return RequestElicitationAsync(request);
	}

	/// <summary>
	/// Simulate a URL-mode elicitation request — shows a link the user must visit.
	/// </summary>
	public Task<ElicitationResult> SimulateUrlModeRequest(string sessionId)
	{
		ElicitationRequestModel request = new()
		{
			SessionId = sessionId,
			Message = "Authorization is required. Please visit the URL below to authenticate.",
			ElicitationSource = "debug-simulator",
			Mode = ElicitationRequestedMode.Url,
			Url = "https://github.com/login/oauth/authorize?client_id=debug&scope=repo",
			Fields = [],
		};
		return RequestElicitationAsync(request);
	}
}
