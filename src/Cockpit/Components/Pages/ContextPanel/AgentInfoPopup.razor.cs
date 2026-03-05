using Cockpit.Components.Controls;
using Cockpit.Features.Agents.Models;
using Cockpit.Utilities;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Pages.ContextPanel;

public partial class AgentInfoPopup : ComponentBase
{
	PopupBase? _popup;
	AgentProfile? _agent;
	string _title = "Agents";

	public void Open(AgentProfile agent)
	{
		_agent = agent;
		_title = agent.DisplayLabel;
		_popup?.Open();

		StateHasChanged();
	}

	void RevealAgentFile()
	{
		FileUtil.RevealFile(_agent?.FilePath);
	}
}
