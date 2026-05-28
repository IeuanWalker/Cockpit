using Cockpit.Components.Controls;
using Cockpit.Features.Byok;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace Cockpit.Components.Popups;

partial class AddCustomModelPopup : ComponentBase
{
	readonly IByokFeature _byokFeature;
	readonly ILogger<AddCustomModelPopup> _logger;

	public AddCustomModelPopup(IByokFeature byokFeature, ILogger<AddCustomModelPopup> logger)
	{
		_byokFeature = byokFeature;
		_logger = logger;
	}

	PopupBase _popup = default!;
	string _name = string.Empty;
	string _modelId = string.Empty;
	string _providerType = "openai";
	string _baseUrl = string.Empty;
	string _apiKey = string.Empty;
	string _wireApi = "completions";
	bool _supportsVision;
	bool _supportsReasoning;
	string _contextWindowInput = string.Empty;
	bool _capabilitiesExpanded;
	string _errorMessage = string.Empty;
	bool _isSaving;

	[Parameter] public EventCallback OnSaved { get; set; }

	public void Open()
	{
		_name = string.Empty;
		_modelId = string.Empty;
		_providerType = "openai";
		_baseUrl = string.Empty;
		_apiKey = string.Empty;
		_wireApi = "completions";
		_supportsVision = false;
		_supportsReasoning = false;
		_contextWindowInput = string.Empty;
		_capabilitiesExpanded = false;
		_errorMessage = string.Empty;
		_isSaving = false;
		_popup.Open();
		StateHasChanged();
	}

	string GetBaseUrlPlaceholder() => _providerType switch
	{
		"azure" => "https://{resource}.openai.azure.com",
		"anthropic" => "https://api.anthropic.com",
		_ => "https://api.openai.com/v1"
	};

	bool IsValid() =>
		!string.IsNullOrWhiteSpace(_name)
		&& !string.IsNullOrWhiteSpace(_modelId)
		&& !string.IsNullOrWhiteSpace(_baseUrl);

	async Task SaveAsync()
	{
		if(!IsValid() || _isSaving)
		{
			return;
		}

		_isSaving = true;
		_errorMessage = string.Empty;
		StateHasChanged();

		try
		{
			int? contextWindow = int.TryParse(_contextWindowInput, out int cw) && cw > 0 ? cw : null;

			ByokModelConfig config = new()
			{
				Id = Guid.NewGuid().ToString(),
				Name = _name.Trim(),
				ModelId = _modelId.Trim(),
				ProviderType = _providerType,
				BaseUrl = _baseUrl.Trim(),
				ApiKey = string.IsNullOrWhiteSpace(_apiKey) ? null : _apiKey.Trim(),
				WireApi = _wireApi,
				SupportsVision = _supportsVision,
				SupportsReasoning = _supportsReasoning,
				MaxContextWindowTokens = contextWindow
			};

			await _byokFeature.AddAsync(config);
			_popup.Close();
			await OnSaved.InvokeAsync();
		}
		catch(Exception ex)
		{
			_errorMessage = $"Failed to save: {ex.Message}";
			_logger.LogError(ex, "Failed to save BYOK model config");
		}
		finally
		{
			_isSaving = false;
			StateHasChanged();
		}
	}

	void Cancel() => _popup.Close();
}
