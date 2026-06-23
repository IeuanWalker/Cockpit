using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Controls;

public partial class CostGradientBar
{
	[Parameter]
	public double? NormalizedCost { get; set; }

	string GetTooltip()
	{
		if(NormalizedCost is null)
		{
			return "Cost tier unavailable";
		}

		return NormalizedCost.Value switch
		{
			< 0.2 => "Very budget-friendly",
			< 0.4 => "Budget-friendly",
			< 0.6 => "Moderate cost",
			< 0.8 => "Premium",
			_ => "Enterprise"
		};
	}
}