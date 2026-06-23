using Cockpit.Features.ElicitationRequests;
using GitHub.Copilot;
using GitHub.Copilot.Rpc;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace Cockpit.Components.Pages.ChatPanel.ElicitationRequest;

public sealed partial class ElicitationFormContent : ComponentBase
{
	[Parameter, EditorRequired] public ElicitationRequestModel Request { get; set; } = default!;
	[Parameter, EditorRequired] public ElicitationFeature Feature { get; set; } = default!;
	[Parameter] public EventCallback OnDone { get; set; }
	[Parameter] public bool ShowMessage { get; set; } = true;

	readonly ILogger<ElicitationFormContent> _logger;

	public ElicitationFormContent(ILogger<ElicitationFormContent> logger)
	{
		_logger = logger;
	}

	readonly Dictionary<string, string> _formValues = [];
	string _lastRequestId = string.Empty;

	protected override void OnParametersSet()
	{
		if(_lastRequestId == Request.Id)
		{
			return;
		}

		_lastRequestId = Request.Id;
		_formValues.Clear();

		foreach(ElicitationSchemaField field in Request.Fields)
		{
			if(field.Default is not null)
			{
				_formValues[field.FieldName] = field.Default;
			}
		}
	}

	string GetValue(string fieldName) => _formValues.GetValueOrDefault(fieldName, string.Empty);
	void SetValue(string fieldName, string value) => _formValues[fieldName] = value;

	bool GetBoolValue(string fieldName) =>
		_formValues.TryGetValue(fieldName, out string? val) && val == "true";

	void SetBoolValue(string fieldName, bool value) => _formValues[fieldName] = value ? "true" : "false";

	bool CanSubmit
	{
		get
		{
			if(Request.Mode?.Value == ElicitationRequestedMode.Url.Value)
			{
				return true;
			}

			foreach(ElicitationSchemaField schemaField in Request.Fields.Where(f => f.IsRequired))
			{
				if(string.IsNullOrWhiteSpace(GetValue(schemaField.FieldName)))
				{
					return false;
				}
			}

			return true;
		}
	}

	async Task OnSubmit()
	{
		Dictionary<string, object> content = [];

		foreach(ElicitationSchemaField field in Request.Fields)
		{
			string rawVal = GetValue(field.FieldName);
			if(string.IsNullOrEmpty(rawVal))
			{
				continue;
			}

			object typedVal = field.Type switch
			{
				ElicitationFieldType.Boolean => rawVal == "true",
				ElicitationFieldType.Integer when int.TryParse(rawVal, out int i) => i,
				ElicitationFieldType.Number when double.TryParse(rawVal, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double d) => d,
				_ => rawVal,
			};
			content[field.FieldName] = typedVal;
		}

		_logger.LogInformation("Submitting elicitation form: {RequestId}, {FieldCount} fields", Request.Id, content.Count);
		Feature.ResolveElicitationRequest(Request.Id, new ElicitationResult
		{
			Action = UIElicitationResponseAction.Accept,
			Content = content,
		});

		await OnDone.InvokeAsync();
	}

	async Task OnDecline()
	{
		_logger.LogInformation("Declining elicitation form: {RequestId}", Request.Id);
		Feature.ResolveElicitationRequest(Request.Id, new ElicitationResult
		{
			Action = UIElicitationResponseAction.Decline,
			Content = new Dictionary<string, object>()
		});

		await OnDone.InvokeAsync();
	}

	async Task OnCancel()
	{
		_logger.LogInformation("Cancelling elicitation form: {RequestId}", Request.Id);
		Feature.ResolveElicitationRequest(Request.Id, null);

		await OnDone.InvokeAsync();
	}

	void OpenInBrowser()
	{
		if(Request.Url is null)
		{
			return;
		}

		try
		{
			System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
			{
				FileName = Request.Url,
				UseShellExecute = true,
			});
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Failed to open URL in browser");
		}
	}
}
