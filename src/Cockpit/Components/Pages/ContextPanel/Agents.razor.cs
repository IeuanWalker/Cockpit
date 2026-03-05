using Cockpit.Features.Agents;
using Cockpit.Features.Agents.Models;
using Cockpit.Features.Sessions;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Pages.ContextPanel;

public sealed partial class Agents : ComponentBase, IDisposable
{
	AgentInfoPopup? _agentInfoPopup;

	readonly GlobalAgentFeature _globalAgentFeature;
	readonly SessionListFeature _sessionListFeature;

	public Agents(GlobalAgentFeature globalAgentFeature, SessionListFeature sessionListFeature)
	{
		_globalAgentFeature = globalAgentFeature;
		_sessionListFeature = sessionListFeature;
	}

	List<AgentProfile> _allAgents = [];
	AgentProfile? _selectedAgent;

	int TotalCount => _allAgents.Count;

	protected override void OnInitialized()
	{
		_sessionListFeature.OnStateChanged += OnStateChanged;
		_globalAgentFeature.OnAgentsChanged += OnStateChanged;
		Refresh();
	}

	void OnStateChanged()
	{
		InvokeAsync(() => { Refresh(); StateHasChanged(); });
	}

	void ShowAgentInfo(AgentProfile agent) => _agentInfoPopup?.Open(agent);

	void Refresh()
	{
		List<AgentProfile> global = [.. _globalAgentFeature.Agents];
		List<AgentProfile> repo = _sessionListFeature.CurrentSession?.Context.RepoAgents ?? [];
		_allAgents = [.. global, .. repo];
		_selectedAgent = _sessionListFeature.CurrentSession?.Context.SelectedAgent;
	}

	public void Dispose()
	{
		_sessionListFeature.OnStateChanged -= OnStateChanged;
		_globalAgentFeature.OnAgentsChanged -= OnStateChanged;
	}
}
