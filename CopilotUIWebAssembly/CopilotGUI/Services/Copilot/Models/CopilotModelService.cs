using GitHub.Copilot.SDK;

namespace CopilotGUI.Services.Copilot.Models;

public class CopilotModelService : ICopilotModelService
{
	List<CopilotModel>? _models;
	public async ValueTask<List<CopilotModel>> GetModels()
	{
		if(_models is not null)
		{
			return _models;
		}

		await using CopilotClient client = new();

		List<ModelInfo> models = await client.ListModelsAsync();

		_models = models.ConvertAll(x => new CopilotModel
		{
			Id = x.Id,
			Name = x.Name,
			BillingMultiplier = x.Billing?.Multiplier ?? 0,
			Capabilities = new CapabilitiesModel
			{
				Supports = new CapabilitiesSupportModel
				{
					ReasoningEffort = x.Capabilities.Supports.ReasoningEffort,
					Vision = x.Capabilities.Supports.Vision
				},
				Limits = new CapabilitiesLimitsModels
				{
					MaxPromptTokens = x.Capabilities.Limits.MaxPromptTokens,
					MaxContextWindowTokens = x.Capabilities.Limits.MaxContextWindowTokens,
					Vision = x.Capabilities.Limits.Vision is null ? null : new VisionLimitsModel
					{
						SupportedMediaTypes = x.Capabilities.Limits.Vision.SupportedMediaTypes,
						MaxPromptImages = x.Capabilities.Limits.Vision.MaxPromptImages,
						MaxPromptImageSize = x.Capabilities.Limits.Vision.MaxPromptImageSize
					}
				}
			},
			SupportedReasoningEfforts = x.SupportedReasoningEfforts,
			DefaultReasoningEffort = x.DefaultReasoningEffort,
		});

		return _models;
	}
}