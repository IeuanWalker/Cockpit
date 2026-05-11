using Cockpit.Features.Agents;
using Cockpit.Features.Agents.Models;
using Cockpit.Features.Sessions;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Pages.ChatPanel.InputArea;

public sealed partial class AgentControl : ComponentBase, IDisposable
{
	readonly AgentPersistence _agentPersistence;
	readonly SessionListFeature _sessionListFeature;

	public AgentControl(
		AgentPersistence agentPersistence,
		SessionListFeature sessionListFeature)
	{
		_agentPersistence = agentPersistence;
		_sessionListFeature = sessionListFeature;
	}

	List<AgentProfile> _allAgents = [];
	PickerControl _picker = default!;

	protected override void OnInitialized()
	{
		_sessionListFeature.OnStateChanged += OnStateChanged;
		RefreshAgents();
	}

	void OnStateChanged()
	{
		InvokeAsync(() => { RefreshAgents(); StateHasChanged(); });
	}

	void RefreshAgents()
	{
		_allAgents = [.. (_sessionListFeature.CurrentSession?.Context.Agents ?? [])
			.Where(a => a.UserInvocable)];
	}

	void SelectAgent(AgentProfile? agent)
	{
		if(_sessionListFeature.CurrentSession is null)
		{
			_picker.Close();
			return;
		}

		AgentProfile? current = _sessionListFeature.CurrentSession.Context.SelectedAgent;

		// No change
		if(agent is null && current is null)
		{
			_picker.Close();
			return;
		}

		if(agent is not null && current is not null && agent.Name == current.Name && agent.Source == current.Source)
		{
			_picker.Close();
			return;
		}

		_sessionListFeature.CurrentSession.Context.SelectedAgent = agent;
		_sessionListFeature.CurrentSession.AgentChanged = true;

		// Persist agent selection immediately
		_ = _agentPersistence.SaveSessionAgentAsync(_sessionListFeature.CurrentSession);

		_picker.Close();
		_sessionListFeature.NotifyStateChanged();
	}

	string GetSelectedAgentLabel()
	{
		AgentProfile? selected = _sessionListFeature.CurrentSession?.Context.SelectedAgent;
		return selected is null ? "Default" : selected.DisplayLabel;
	}

	public void Dispose()
	{
		_sessionListFeature.OnStateChanged -= OnStateChanged;
	}
}
