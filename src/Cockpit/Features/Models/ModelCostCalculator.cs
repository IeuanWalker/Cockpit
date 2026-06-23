using GitHub.Copilot;

namespace Cockpit.Features.Models;

/// <summary>
/// Calculates cost visualization based on relative model pricing.
/// Cheapest model = green, most expensive = red, with gradient in between.
/// </summary>
public static class ModelCostCalculator
{
	/// <summary>
	/// Gets the normalized cost position (0-1) for a model relative to all available models.
	/// 0 = cheapest (green), 1 = most expensive (red)
	/// </summary>
	public static double? GetNormalizedCost(ModelInfo model, IReadOnlyList<ModelInfo> allModels)
	{
		if(model?.Billing is null)
		{
			return null;
		}

		double? modelPrice = ExtractPrice(model);
		if(modelPrice is null)
		{
			return null;
		}

		// Get min and max prices from all models
		double minPrice = double.MaxValue;
		double maxPrice = 0;
		bool hasAnyPrice = false;

		foreach(ModelInfo m in allModels)
		{
			double? price = ExtractPrice(m);
			if(price is not null && price >= 0)
			{
				hasAnyPrice = true;
				minPrice = Math.Min(minPrice, price.Value);
				maxPrice = Math.Max(maxPrice, price.Value);
			}
		}

		if(!hasAnyPrice || minPrice == maxPrice)
		{
			// All same price or no prices available
			return 0.5;
		}

		// Normalize to 0-1 range
		double normalized = (modelPrice.Value - minPrice) / (maxPrice - minPrice);
		return Math.Clamp(normalized, 0, 1);
	}

	/// <summary>
	/// Gets the gradient color for a cost position (0-1).
	/// Green (0) → Yellow → Orange → Red (1)
	/// </summary>
	public static string GetGradientColor(double? normalizedCost)
	{
		if(normalizedCost is null)
		{
			return "#888888"; // Grey for unknown
		}

		double position = normalizedCost.Value;

		// Define color stops: Green → Yellow → Orange → Red
		if(position < 0.33)
		{
			// Green to Yellow (0 to 0.33)
			double t = position / 0.33;
			return InterpolateColor("#4ade80", "#fbbf24", t);
		}
		else if(position < 0.66)
		{
			// Yellow to Orange (0.33 to 0.66)
			double t = (position - 0.33) / 0.33;
			return InterpolateColor("#fbbf24", "#f97316", t);
		}
		else
		{
			// Orange to Red (0.66 to 1.0)
			double t = (position - 0.66) / 0.34;
			return InterpolateColor("#f97316", "#ef4444", t);
		}
	}

	/// <summary>
	/// Gets a human-readable cost label based on normalized position.
	/// </summary>
	public static string GetCostLabel(double? normalizedCost)
	{
		if(normalizedCost is null)
		{
			return "Unknown cost";
		}

		return normalizedCost.Value switch
		{
			< 0.2 => "Very budget-friendly",
			< 0.4 => "Budget-friendly",
			< 0.6 => "Moderate cost",
			< 0.8 => "Premium",
			_ => "Enterprise"
		};
	}

	/// <summary>
	/// Returns the cheapest model based on input token price, or <see langword="null"/> if no model has pricing information.
	/// </summary>
	public static ModelInfo? GetCheapestModel(IReadOnlyList<ModelInfo> models)
	{
		ModelInfo? cheapest = null;
		double cheapestPrice = double.MaxValue;

		foreach(ModelInfo m in models)
		{
			double? price = ExtractPrice(m);
			if(price is not null && price.Value < cheapestPrice)
			{
				cheapestPrice = price.Value;
				cheapest = m;
			}
		}

		return cheapest;
	}

	/// <summary>
	/// Extracts the pricing value from a model for comparison.
	/// </summary>
	static double? ExtractPrice(ModelInfo model)
	{
		// Use token-based pricing if available
		if(model.Billing?.TokenPrices?.InputPrice is not null)
		{
			return model.Billing.TokenPrices.InputPrice.Value;
		}

		return null;
	}

	/// <summary>
	/// Linearly interpolates between two hex colors.
	/// t = 0 returns color1, t = 1 returns color2
	/// </summary>
	static string InterpolateColor(string hex1, string hex2, double t)
	{
		// Parse hex colors to RGB
		int r1 = int.Parse(hex1.Substring(1, 2), System.Globalization.NumberStyles.HexNumber);
		int g1 = int.Parse(hex1.Substring(3, 2), System.Globalization.NumberStyles.HexNumber);
		int b1 = int.Parse(hex1.Substring(5, 2), System.Globalization.NumberStyles.HexNumber);

		int r2 = int.Parse(hex2.Substring(1, 2), System.Globalization.NumberStyles.HexNumber);
		int g2 = int.Parse(hex2.Substring(3, 2), System.Globalization.NumberStyles.HexNumber);
		int b2 = int.Parse(hex2.Substring(5, 2), System.Globalization.NumberStyles.HexNumber);

		// Interpolate
		int r = (int)(r1 + (r2 - r1) * t);
		int g = (int)(g1 + (g2 - g1) * t);
		int b = (int)(b1 + (b2 - b1) * t);

		return $"#{r:x2}{g:x2}{b:x2}";
	}
}

