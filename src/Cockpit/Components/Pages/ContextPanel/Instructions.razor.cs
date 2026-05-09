using Cockpit.Features.Sessions;
using Cockpit.Features.Sessions.Models;
using GitHub.Copilot.SDK.Rpc;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Pages.ContextPanel;

public sealed partial class Instructions : ComponentBase, IDisposable
{
	InstructionInfoPopup? _instructionInfoPopup;
	readonly SessionListFeature _sessionListFeature;

	public Instructions(SessionListFeature sessionListFeature)
	{
		_sessionListFeature = sessionListFeature;
	}

	List<InstructionsSources> _allInstructions = [];

	int TotalCount => _allInstructions.Count;

	protected override void OnInitialized()
	{
		_sessionListFeature.OnStateChanged += OnStateChanged;
		Refresh();
	}

	void OnStateChanged()
	{
		InvokeAsync(() => { Refresh(); StateHasChanged(); });
	}

	void ShowInstructionInfo(InstructionsSources instruction)
	{
		SessionContext? ctx = _sessionListFeature.CurrentSession?.Context;
		string? repoRoot = ctx?.GitRoot ?? ctx?.CurrentWorkingDirectory;
		_instructionInfoPopup?.Open(_allInstructions, instruction, repoRoot);
	}

	List<InstructionsSources> _renderedInstructions = [];

	protected override bool ShouldRender()
	{
		if(ReferenceEquals(_allInstructions, _renderedInstructions))
		{
			return false;
		}

		_renderedInstructions = _allInstructions;
		return true;
	}

	void Refresh()
	{
		_allInstructions = [.. _sessionListFeature.CurrentSession?.Context.Instructions ?? []];
	}

	public void Dispose()
	{
		_sessionListFeature.OnStateChanged -= OnStateChanged;
	}
}
