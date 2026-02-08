namespace CopilotGUI.Services.Copilot.Models;

public interface ICopilotModelService
{
	ValueTask<List<CopilotModel>> GetModels();
}
