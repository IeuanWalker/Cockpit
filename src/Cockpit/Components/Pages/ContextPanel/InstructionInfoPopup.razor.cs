using Cockpit.Components.Controls;
using Cockpit.Utilities;
using GitHub.Copilot.SDK.Rpc;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Pages.ContextPanel;

public partial class InstructionInfoPopup : ComponentBase
{
	PopupBase? _popup;
	InstructionsSources? _selectedInstruction;
	List<InstructionsSources> _instructions = [];

	public void Open(IReadOnlyList<InstructionsSources> instructions, InstructionsSources selectedInstruction)
	{
		_instructions = [.. instructions];
		_popup?.Open();
		SelectInstruction(selectedInstruction);
	}

	void SelectInstruction(InstructionsSources instruction)
	{
		_selectedInstruction = instruction;
		StateHasChanged();
	}

	void RevealInstructionFile() => FileUtil.RevealFile(_selectedInstruction?.SourcePath);
}
