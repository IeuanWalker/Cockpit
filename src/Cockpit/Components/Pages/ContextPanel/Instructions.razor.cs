using Cockpit.Features.Sessions;
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
	InstructionsSources? _selectedInstruction;

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
	InstructionsSources? _renderedSelected;

	protected override bool ShouldRender()
	{
		if(ReferenceEquals(_allInstructions, _renderedInstructions) && ReferenceEquals(_renderedSelected, _selectedInstruction))
			return false;
		_renderedInstructions = _allInstructions;
		_renderedSelected = _selectedInstruction;
		return true;
	}

	void Refresh()
	{
		_allInstructions = [.. _sessionListFeature.CurrentSession?.Context.Instructions ?? []];
		_selectedInstruction = null;
	}

	public void Dispose()
	{
		_sessionListFeature.OnStateChanged -= OnStateChanged;
	}
}
