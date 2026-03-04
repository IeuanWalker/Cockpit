using System.Diagnostics;
using Cockpit.Components.Controls;
using Cockpit.Features.Agents.Models;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Pages.ContextPanel;

public partial class AgentInfoPopup : ComponentBase
{
	PopupBase? _popup;
	AgentProfile? _agent;

	public void Open(AgentProfile agent)
	{
		_agent = agent;
		_popup?.Open();
	}

	void RevealAgentFile()
	{
		Cockpit.Utilities.FileUtil.RevealFile(_agent?.FilePath);
	}
}
