using GitHub.Copilot.SDK;

namespace CopilotGUI.Services.Copilot.Models;


public sealed class CopilotModel
{
	public required string Id { get; set; }
	public required string Name { get; set; }
	public required double BillingMultiplier { get; set; }
	public required CapabilitiesModel Capabilities { get; set; }
	public ModelPolicy? Policy { get; set; }
	public ModelBilling? Billing { get; set; }
	public required List<string>? SupportedReasoningEfforts { get; set; }
	public required string? DefaultReasoningEffort { get; set; }
}

public sealed class CapabilitiesModel
{
	public required CapabilitiesSupportModel Supports { get; set; }
	public required CapabilitiesLimitsModels Limits { get; set; }
}

public sealed class CapabilitiesSupportModel
{
	public required bool Vision { get; set; }
	public required bool ReasoningEffort { get; set; }
}
public sealed class CapabilitiesLimitsModels
{
	public required int? MaxPromptTokens { get; set; }
	public required int MaxContextWindowTokens { get; set; }
	public VisionLimitsModel? Vision { get; set; }
}

public sealed class VisionLimitsModel
{
	public required List<string> SupportedMediaTypes { get; set; }
	public required int MaxPromptImages { get; set; }
	public required int MaxPromptImageSize { get; set; }
}


