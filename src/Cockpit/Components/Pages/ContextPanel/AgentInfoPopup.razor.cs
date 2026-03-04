using Cockpit.Components.Controls;
using Cockpit.Features.Agents.Models;
using Microsoft.AspNetCore.Components;
using System.Diagnostics;

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
		if(_agent?.FilePath is not string path || string.IsNullOrWhiteSpace(path)) return;
		try
		{
			if(OperatingSystem.IsWindows())
			{
				Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"/select,\"{path}\"", UseShellExecute = true });
			}
			else if(OperatingSystem.IsMacOS())
			{
				Process.Start(new ProcessStartInfo { FileName = "open", Arguments = $"-R \"{path}\"", UseShellExecute = false });
			}
		}
		catch { /* best-effort */ }
	}
}
