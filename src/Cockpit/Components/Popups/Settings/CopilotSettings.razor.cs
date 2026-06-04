using Cockpit.Features.AppSettings;
using Cockpit.Features.SystemMessage;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Popups.Settings;

partial class CopilotSettings : ComponentBase, IDisposable
{
	static readonly (string Key, string DisplayName, string Description)[] sections =
	[
		("identity",             "Identity",             "Agent identity preamble and mode statement"),
		("tone",                 "Tone",                 "Response style, conciseness rules, output formatting preferences"),
		("tool_efficiency",      "Tool Efficiency",      "Tool usage patterns, parallel calling, batching guidelines"),
		("environment_context",  "Environment",          "Working directory, OS, git root, directory listing, available tools"),
		("code_change_rules",    "Code Change Rules",    "Coding rules, linting/testing, ecosystem tools, style"),
		("guidelines",           "Guidelines",           "Tips, behavioural best practices"),
		("safety",               "Safety",               "Environment limitations, prohibited actions, security policies"),
		("tool_instructions",    "Tool Instructions",    "Per-tool usage instructions"),
		("custom_instructions",  "Custom Instructions",  "Repository and organisation custom instructions"),
		("runtime_instructions", "Runtime Instructions", "End-of-prompt runtime instructions"),
		("last_instructions",    "Last Instructions",    "Final instructions: parallel tool calling, persistence, task completion"),
	];

	readonly IAppSettingsFeature _appSettingsFeature;
	readonly ISystemMessageFeature _systemMessageFeature;

	public CopilotSettings(IAppSettingsFeature appSettingsFeature, ISystemMessageFeature systemMessageFeature)
	{
		_appSettingsFeature = appSettingsFeature;
		_systemMessageFeature = systemMessageFeature;
	}

	bool _showRestartWarning;
	readonly HashSet<string> _expandedDefaults = [];

	protected override void OnInitialized()
	{
		_systemMessageFeature.OnDefaultsLoaded += OnDefaultsLoaded;
	}

	void OnDefaultsLoaded()
	{
		InvokeAsync(StateHasChanged);
	}

	public void Dispose()
	{
		Dispose(true);
		GC.SuppressFinalize(this);
	}

	protected virtual void Dispose(bool disposing)
	{
		if(disposing)
		{
			_systemMessageFeature.OnDefaultsLoaded -= OnDefaultsLoaded;
		}
	}

	SystemMessageSectionSetting GetSectionSetting(string key)
	{
		Dictionary<string, SystemMessageSectionSetting> overrides = _appSettingsFeature.SystemMessageSectionOverrides;
		if(overrides.TryGetValue(key, out SystemMessageSectionSetting? setting))
		{
			return setting;
		}

		return new SystemMessageSectionSetting();
	}

	void SaveSectionSetting(string key, SystemMessageSectionSetting setting)
	{
		Dictionary<string, SystemMessageSectionSetting> overrides = _appSettingsFeature.SystemMessageSectionOverrides;
		overrides[key] = setting;
		_appSettingsFeature.SystemMessageSectionOverrides = overrides;
		_showRestartWarning = true;
	}

	void OnActionChanged(string key, ChangeEventArgs e)
	{
		if(!Enum.TryParse(e.Value?.ToString(), out SystemMessageOverrideAction action))
		{
			return;
		}

		SystemMessageSectionSetting existing = GetSectionSetting(key);
		SaveSectionSetting(key, new SystemMessageSectionSetting
		{
			Action = action,
			Content = existing.Content,
		});
	}

	void OnContentChanged(string key, ChangeEventArgs e)
	{
		SystemMessageSectionSetting existing = GetSectionSetting(key);
		SaveSectionSetting(key, new SystemMessageSectionSetting
		{
			Action = existing.Action,
			Content = e.Value?.ToString() ?? string.Empty,
		});
	}

	void ToggleDefaultExpanded(string key)
	{
		if(!_expandedDefaults.Remove(key))
		{
			_expandedDefaults.Add(key);
		}
	}
}
