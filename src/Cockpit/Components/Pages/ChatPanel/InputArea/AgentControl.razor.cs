using Cockpit.Features.Agents;
using Cockpit.Features.Agents.Models;
using Cockpit.Features.Sessions;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Pages.ChatPanel;

public partial class AgentControl : ComponentBase, IDisposable
{
	readonly AgentPersistence _agentPersistence;
	readonly GlobalAgentFeature _globalAgentFeature;
	readonly SessionListFeature _sessionListFeature;

	public AgentControl(
		AgentPersistence agentPersistence,
		GlobalAgentFeature globalAgentFeature,
		SessionListFeature sessionListFeature)
	{
		_agentPersistence = agentPersistence;
		_globalAgentFeature = globalAgentFeature;
		_sessionListFeature = sessionListFeature;
	}

	List<AgentProfile> _allAgents = [];
	bool _isDropdownOpen;

	protected override void OnInitialized()
	{
		_sessionListFeature.OnStateChanged += OnStateChanged;
		_globalAgentFeature.OnAgentsChanged += OnAgentsChanged;
		RefreshAgents();
	}

	void OnStateChanged()
	{
		InvokeAsync(() => { RefreshAgents(); StateHasChanged(); });
	}

	void OnAgentsChanged()
	{
		InvokeAsync(() => { RefreshAgents(); StateHasChanged(); });
	}

	void RefreshAgents()
	{
		List<AgentProfile> global = [.. _globalAgentFeature.Agents];
		List<AgentProfile> repo = _sessionListFeature.CurrentSession?.Context.RepoAgents ?? [];
		_allAgents = [.. global, .. repo];
	}

	void ToggleDropdown()
	{
		_isDropdownOpen = !_isDropdownOpen;
	}

	void CloseDropdown()
	{
		_isDropdownOpen = false;
	}

	void SelectAgent(AgentProfile? agent)
	{
		if(_sessionListFeature.CurrentSession is null)
		{
			_isDropdownOpen = false;
			return;
		}

		AgentProfile? current = _sessionListFeature.CurrentSession.Context.SelectedAgent;

		// No change
		if(agent is null && current is null)
		{
			_isDropdownOpen = false;
			return;
		}

		if(agent is not null && current is not null && agent.Config.Name == current.Config.Name && agent.Source == current.Source)
		{
			_isDropdownOpen = false;
			return;
		}

		_sessionListFeature.CurrentSession.Context.SelectedAgent = agent;
		_sessionListFeature.CurrentSession.AgentChanged = true;

		// Persist agent selection immediately
		_ = _agentPersistence.SaveSessionAgent(_sessionListFeature.CurrentSession);

		_isDropdownOpen = false;
		_sessionListFeature.NotifyStateChanged();
	}

	string GetSelectedAgentLabel()
	{
		AgentProfile? selected = _sessionListFeature.CurrentSession?.Context.SelectedAgent;
		return selected is null ? "Default" : selected.DisplayLabel;
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
			_sessionListFeature.OnStateChanged -= OnStateChanged;
			_globalAgentFeature.OnAgentsChanged -= OnAgentsChanged;
		}
	}
}
