using Cockpit.Features.SystemMessage;
using GitHub.Copilot;

namespace Cockpit.Features.Sessions;

public sealed partial class SessionFeature
{
	void ApplySystemMessageCustomization(SessionConfig config, ModelInfo model)
		=> config.SystemMessage = CreateSystemMessageConfig(model.Name, model.Id);

	void ApplySystemMessageCustomization(ResumeSessionConfig config, ModelInfo model)
		=> config.SystemMessage = CreateSystemMessageConfig(model.Name, model.Id);

	void ApplySystemMessageCustomization(SessionConfig config, ModelInfo? model, string fallbackModelId)
		=> config.SystemMessage = CreateSystemMessageConfig(model?.Name ?? fallbackModelId, model?.Id ?? fallbackModelId);

	void ApplySystemMessageCustomization(ResumeSessionConfig config, ModelInfo? model, string fallbackModelId)
		=> config.SystemMessage = CreateSystemMessageConfig(model?.Name ?? fallbackModelId, model?.Id ?? fallbackModelId);

	SystemMessageConfig CreateSystemMessageConfig(string modelName, string modelId)
	{
		Dictionary<SystemMessageSection, SectionOverride> sections = [];
		foreach(KeyValuePair<string, SystemMessageSectionSetting> kvp in _appSettingsFeature.SystemMessageSectionOverrides)
		{
			SectionOverride? sectionOverride = MapToSectionOverride(kvp.Value);
			if(sectionOverride is not null)
			{
				sections[new SystemMessageSection(kvp.Key)] = sectionOverride;
			}
		}

		sections[SystemMessageSection.Identity] = new SectionOverride()
		{
			Action = SectionOverrideAction.Replace,
			Content = $"""
				You are Cockpit, a desktop application that provides a graphical interface for GitHub Copilot CLI. You are an interactive AI assistant that helps users with software engineering tasks through a desktop GUI experience.

				# Search and delegation

				* When prompting sub-agents, provide comprehensive context — brevity rules do not apply to sub-agent prompts.
				* When searching the file system for files or text, stay in the current working directory or child directories of the cwd unless absolutely necessary.
				* When searching code, the preference order for tools to use is: code intelligence tools (if available) > LSP-based tools (if available) > glob > rg with glob pattern > powershell tool.

				Powered by <model name="{modelName}" id="{modelId}" />.

				When asked who you are, reply with something like: "I'm Cockpit, a desktop application that provides a graphical interface for GitHub Copilot CLI, powered by GPT-5.4 mini (model ID: gpt-5.4-mini)."

				When asked which model is being used, reply with something like: "I'm powered by {modelName} (model ID: {modelId})."

				If the model was changed during the conversation, acknowledge the change and respond accordingly.

				Your job is to perform the task the user requested while providing a desktop-first experience through Cockpit.
			"""
		};

		return new SystemMessageConfig
		{
			Mode = SystemMessageMode.Customize,
			Sections = sections
		};
	}
}
