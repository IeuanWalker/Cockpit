using GitHub.Copilot.SDK;

namespace Cockpit.Services.Copilot;

public class CopilotModelService
{
	List<ModelInfo>? _models;
	public async ValueTask<List<ModelInfo>> GetModels()
	{
		if(_models is not null)
		{
			return _models;
		}

		await using CopilotClient client = new();

		_models = await client.ListModelsAsync();

		return _models;
	}

	public async ValueTask<ModelInfo> GetDefaultModel()
	{
		List<ModelInfo>? models = _models;

		if(_models is null)
		{
			models = await GetModels();
		}

		return models!.First();
	}
}