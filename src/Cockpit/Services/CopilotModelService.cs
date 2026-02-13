using GitHub.Copilot.SDK;

namespace Cockpit.Services;

public class CopilotModelService
{
	List<ModelInfo>? _models;
	/// <summary>
	/// Get all models
	/// </summary>
	/// <returns></returns>
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

	/// <summary>
	/// Get Default model
	/// </summary>
	/// <returns></returns>
	/// <remarks>
	/// TODO: Impelement functionality too allow the user to select the default model
	/// </remarks>
	public async ValueTask<ModelInfo> GetDefaultModel()
	{
		List<ModelInfo>? models = _models;

		if(_models is null)
		{
			models = await GetModels();
		}

		return models!.Where(x => x.Billing?.Multiplier == 0).ToList()[1];
	}
}