using Blazor.Sonner.Services;
using Cockpit.Components.Controls;
using Cockpit.Extensions;
using Cockpit.Features.Byok;
using Cockpit.Features.Models;
using GitHub.Copilot.SDK;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Cockpit.Components.Popups;

public sealed partial class ModelInfoPopup : ComponentBase, IDisposable
{
	readonly IByokFeature _byokFeature;
	readonly IModelFeature _modelFeature;
	readonly ToastService _toastService;
	readonly IJSRuntime _jsRuntime;
	bool _isDisposed;

	public ModelInfoPopup(IByokFeature byokFeature, IModelFeature modelFeature, ToastService toastService, IJSRuntime jsRuntime)
	{
		_byokFeature = byokFeature;
		_modelFeature = modelFeature;
		_toastService = toastService;
		_jsRuntime = jsRuntime;
		_byokFeature.OnChanged += HandleByokChanged;
	}

	[Parameter] public IReadOnlyList<ModelInfo> Models { get; set; } = [];
	[Parameter] public ModelInfo? SelectedModel { get; set; }
	[Parameter] public EventCallback<ModelInfo> OnModelSelected { get; set; }
	[Parameter] public double MaxMultiplier { get; set; }

	PopupBase _popup = default!;
	PopupBase _jsonPopup = default!;
	AddCustomModelPopup _addModelPopup = default!;
	string _searchFilter = string.Empty;
	string _filterOption = "All";
	bool _filterDropdownOpen = false;
	ModelInfo? _jsonModel;
	IReadOnlyList<ModelInfo>? _localModels;
	string? _pendingScrollModelId;

	public void Open()
	{
		_searchFilter = string.Empty;
		_filterOption = "All";
		_filterDropdownOpen = false;
		_ = RefreshModelsAsync();
		_popup.Open();
		StateHasChanged();
	}

	public void Dispose()
	{
		_isDisposed = true;
		_byokFeature.OnChanged -= HandleByokChanged;
		GC.SuppressFinalize(this);
	}

	IReadOnlyList<ModelInfo> GetFilteredModels()
	{
		IEnumerable<ModelInfo> result = _localModels ?? Models;
		if(!string.IsNullOrWhiteSpace(_searchFilter))
		{
			result = result.Where(m =>
				m.Name.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase)
				|| m.Id.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase));
		}
		result = _filterOption switch
		{
			"Vision" => result.Where(m => m.Capabilities?.Supports?.Vision == true),
			"Reasoning" => result.Where(m => m.Capabilities?.Supports?.ReasoningEffort == true),
			_ => result,
		};
		return [.. result];
	}

	string GetMultiplierColor(ModelInfo model)
	{
		if(model.Billing is null)
		{
			return "#999999";
		}

		double multiplier = model.Billing.Multiplier;

		if(multiplier == 0)
		{
			return "#00ff00";
		}

		if(multiplier < 1)
		{
			return "#00d000";
		}

		if(multiplier == 1)
		{
			return "#999999";
		}

		if(MaxMultiplier > 1 && multiplier >= MaxMultiplier)
		{
			return "#FF0000";
		}

		return "#ff8c00";
	}

	string GetMultiplierDisplay(ModelInfo model)
	{
		if(model.Billing is null)
		{
			return string.Empty;
		}

		if(model.Id.Equals("Auto", StringComparison.OrdinalIgnoreCase))
		{
			return string.Empty;
		}

		return $"{model.Billing.Multiplier:0.0}x";
	}

	async Task SelectModelFromPopup(ModelInfo model)
	{
		_popup.Close();
		await OnModelSelected.InvokeAsync(model);
	}

	void ShowJsonPopup(ModelInfo model)
	{
		_jsonModel = model;
		_jsonPopup.Open();
	}

	string GetRawJson(ModelInfo model) => model.SerializeJson() ?? string.Empty;

	ByokModelConfig? GetByokConfig(string modelId) =>
		_byokFeature.GetAll().FirstOrDefault(c => c.ModelId == modelId);

	string GetProviderLabel(string providerType) => providerType switch
	{
		"azure" => "Azure",
		"anthropic" => "Anthropic",
		_ => "OpenAI"
	};

	(string bg, string text, string border) GetProviderBadgeStyle(string providerType) => providerType switch
	{
		"azure" => ("rgba(59,130,246,0.1)", "rgb(96,165,250)", "rgba(59,130,246,0.3)"),
		"anthropic" => ("rgba(245,158,11,0.1)", "rgb(251,191,36)", "rgba(245,158,11,0.3)"),
		_ => ("rgba(16,185,129,0.1)", "rgb(52,211,153)", "rgba(16,185,129,0.3)")
	};

	void OpenAddModelPopup() => _addModelPopup.Open();

	async Task DeleteByokModel(ByokModelConfig config)
	{
		await _byokFeature.RemoveAsync(config.Id);
		await RefreshModelsAsync();
		StateHasChanged();
	}

	async Task OnModelAdded(string modelId)
	{
		await RefreshModelsAsync();
		_pendingScrollModelId = modelId;
		_toastService.Success("Model added", opts => opts.Description = "Custom model was added successfully.");
		StateHasChanged();
	}

	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		if(_pendingScrollModelId is not null)
		{
			string elementId = $"model-card-{Uri.EscapeDataString(_pendingScrollModelId)}";
			_pendingScrollModelId = null;
			await _jsRuntime.InvokeVoidAsync("cockpit.scrollIntoView", elementId);
		}
	}

	void HandleByokChanged()
	{
		if(_isDisposed)
		{
			return;
		}

		_ = InvokeAsync(async () =>
		{
			if(_isDisposed)
			{
				return;
			}

			await RefreshModelsAsync();
			StateHasChanged();
		});
	}

	async Task RefreshModelsAsync()
	{
		_localModels = await _modelFeature.GetModels();
	}
}
