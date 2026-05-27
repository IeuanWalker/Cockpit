using Cockpit.Components.Controls;
using GitHub.Copilot.SDK;
using Microsoft.AspNetCore.Components;
using System.Text.Json;

namespace Cockpit.Components.Popups;

public partial class ModelInfoPopup : ComponentBase
{
	[Parameter] public IReadOnlyList<ModelInfo> Models { get; set; } = [];
	[Parameter] public ModelInfo? SelectedModel { get; set; }
	[Parameter] public EventCallback<ModelInfo> OnModelSelected { get; set; }
	[Parameter] public double MaxMultiplier { get; set; }

	PopupBase _popup = default!;
	PopupBase _jsonPopup = default!;
	string _searchFilter = string.Empty;
	string _filterOption = "All";
	bool _filterDropdownOpen = false;
	ModelInfo? _jsonModel;

	public void Open()
	{
		_searchFilter = string.Empty;
		_filterOption = "All";
		_filterDropdownOpen = false;
		_popup.Open();
		StateHasChanged();
	}

	IReadOnlyList<ModelInfo> GetFilteredModels()
	{
		IEnumerable<ModelInfo> result = Models;
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
			return "Unknown";
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

	string GetRawJson(ModelInfo model) =>
		JsonSerializer.Serialize(model, new JsonSerializerOptions { WriteIndented = true });
}
