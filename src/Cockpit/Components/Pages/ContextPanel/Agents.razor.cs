using Cockpit.Features.Agents.Models;
using Cockpit.Features.Sessions;
using Cockpit.Features.Sessions.Models;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Pages.ContextPanel;

public sealed partial class Agents : ComponentBase, IDisposable
{
	AgentInfoPopup? _agentInfoPopup;

	readonly SessionListFeature _sessionListFeature;

	public Agents(SessionListFeature sessionListFeature)
	{
		_sessionListFeature = sessionListFeature;
	}

	List<AgentProfile> _allAgents = [];
	AgentProfile? _selectedAgent;

	int TotalCount => _allAgents.Count;

	protected override void OnInitialized()
	{
		_sessionListFeature.OnStateChanged += OnStateChanged;
		Refresh();
	}

	void OnStateChanged()
	{
		InvokeAsync(() =>
		{
			Refresh();
			StateHasChanged();
		});
	}

	void ShowAgentInfo(AgentProfile agent) => _agentInfoPopup?.Open(_allAgents, agent);

	AgentProfile? _renderedSelectedAgent;
	List<AgentProfile> _renderedAgents = [];

	protected override bool ShouldRender()
	{
		if(ReferenceEquals(_allAgents, _renderedAgents) && ReferenceEquals(_renderedSelectedAgent, _selectedAgent))
		{
			return false;
		}

		_renderedAgents = _allAgents;
		_renderedSelectedAgent = _selectedAgent;
		return true;
	}

	void Refresh()
	{
		SessionModel? session = _sessionListFeature.CurrentSession;
		_allAgents = [.. session?.Context.Agents ?? []];
		_selectedAgent = session?.Context.SelectedAgent;
	}

	public void Dispose()
	{
		_sessionListFeature.OnStateChanged -= OnStateChanged;
	}
}
