using GitHub.Copilot.SDK;

namespace Cockpit.Services.Copilot.Models;

public interface ICopilotModelService
{
	ValueTask<List<ModelInfo>> GetModels();
}
