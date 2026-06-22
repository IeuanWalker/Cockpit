using Cockpit.Components.Controls;
using Cockpit.Features.Models;
using GitHub.Copilot;

namespace Cockpit.Components.Popups;

public partial class ModelCostPopup
{
	PopupBase _popup = default!;
	ModelInfo? _model;
	IReadOnlyList<ModelInfo>? _allModels;

	public void Open(ModelInfo model, IReadOnlyList<ModelInfo> allModels)
	{
		_model = model;
		_allModels = allModels;
		_popup.Open();
	}

	double? GetNormalizedCost(ModelInfo model)
	{
		return ModelCostCalculator.GetNormalizedCost(model, _allModels ?? []);
	}

	string? GetCostColor(ModelInfo model)
	{
		// For single model, use middle of spectrum or extract from pricing
		double? cost = GetNormalizedCost(model);
		return cost is not null ? ModelCostCalculator.GetGradientColor(cost) : null;
	}

	string GetCostTierLabel(double cost)
	{
		return cost switch
		{
			< 0.2 => "Very budget-friendly",
			< 0.4 => "Budget-friendly",
			< 0.6 => "Moderate cost",
			< 0.8 => "Premium",
			_ => "Enterprise"
		};
	}
}