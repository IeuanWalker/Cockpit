using Cockpit.Components.Controls;
using Cockpit.Features.ElicitationRequests;
using GitHub.Copilot;
using GitHub.Copilot.Rpc;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace Cockpit.Components.Pages.ChatPanel.ElicitationRequest;

public sealed partial class ElicitationFormPopup : ComponentBase
{
	[Parameter] public ElicitationRequestModel? Request { get; set; }
	[Parameter] public ElicitationFeature? ElicitationFeature { get; set; }

	readonly ILogger<ElicitationFormPopup> _logger;

	public ElicitationFormPopup(ILogger<ElicitationFormPopup> logger)
	{
		_logger = logger;
	}

	PopupBase _popup = default!;

	/// <summary>
	/// The captured request at the time the popup was opened.
	/// Prevents submitting to the wrong request if multiple are pending.
	/// </summary>
	ElicitationRequestModel? _request;

	readonly Dictionary<string, string> _formValues = [];

	public void Open(ElicitationRequestModel request)
	{
		_request = request;
		_formValues.Clear();

		// Pre-populate defaults
		foreach(ElicitationSchemaField field in request.Fields)
		{
			if(field.Default is not null)
			{
				_formValues[field.FieldName] = field.Default;
			}
		}

		_popup.Open();
	}

	string GetTitle() => _request is not null
		? $"Form from {_request.ElicitationSource}"
		: "Form Request";

	string GetValue(string fieldName) => _formValues.GetValueOrDefault(fieldName, string.Empty);
	void SetValue(string fieldName, string value) => _formValues[fieldName] = value;

	bool GetBoolValue(string fieldName) =>
		_formValues.TryGetValue(fieldName, out string? val)
			? val == "true"
			: false;

	void SetBoolValue(string fieldName, bool value) => _formValues[fieldName] = value ? "true" : "false";

	bool CanSubmit
	{
		get
		{
			if(_request is null)
			{
				return false;
			}

			foreach(ElicitationSchemaField schemaField in _request.Fields.Where(f => f.IsRequired))
			{
				string val = GetValue(schemaField.FieldName);
				if(string.IsNullOrWhiteSpace(val))
				{
					return false;
				}
			}

			return true;
		}
	}

	void OnSubmit()
	{
		if(_request is null || ElicitationFeature is null)
		{
			return;
		}

		Dictionary<string, object> content = [];
		foreach(ElicitationSchemaField field in _request.Fields)
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

		ElicitationResult result = new()
		{
			Action = UIElicitationResponseAction.Accept,
			Content = content,
		};

		_logger.LogInformation("Submitting elicitation form: {RequestId}, {FieldCount} fields", _request.Id, content.Count);
		ElicitationFeature.ResolveElicitationRequest(_request.Id, result);
		_popup.Close();
	}

	void OnDecline()
	{
		if(_request is null || ElicitationFeature is null)
		{
			return;
		}

		_logger.LogInformation("Declining elicitation form: {RequestId}", _request.Id);
		ElicitationFeature.ResolveElicitationRequest(_request.Id, new ElicitationResult
		{
			Action = UIElicitationResponseAction.Decline,
			Content = new Dictionary<string, object>()
		});
		_popup.Close();
	}

	void OnCancel()
	{
		if(_request is null || ElicitationFeature is null)
		{
			return;
		}

		_logger.LogInformation("Cancelling elicitation form: {RequestId}", _request.Id);
		ElicitationFeature.ResolveElicitationRequest(_request.Id, null);
		_popup.Close();
	}

	void OpenInBrowser()
	{
		if(_request?.Url is null)
		{
			return;
		}

		try
		{
			System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
			{
				FileName = _request.Url,
				UseShellExecute = true,
			});
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Failed to open URL in browser");
		}
	}
}
