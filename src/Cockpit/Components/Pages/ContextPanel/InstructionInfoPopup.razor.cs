using Cockpit.Components.Controls;
using GitHub.Copilot.SDK.Rpc;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Pages.ContextPanel;

public partial class InstructionInfoPopup : ComponentBase
{
	PopupBase? _popup;
	InstructionsSources? _selectedInstruction;
	List<InstructionsSources> _instructions = [];
	readonly Dictionary<string, bool> _expandedGroups = new(StringComparer.OrdinalIgnoreCase);

	Dictionary<string, List<InstructionsSources>> _groupedInstructions = [];

	public void Open(IReadOnlyList<InstructionsSources> instructions, InstructionsSources selectedInstruction)
	{
		_instructions = [.. instructions];
		_groupedInstructions = _instructions
			.GroupBy(s => s.Location.ToString(), StringComparer.OrdinalIgnoreCase)
			.ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
		_popup?.Open();
		SelectInstruction(selectedInstruction);
	}

	void SelectInstruction(InstructionsSources instruction)
	{
		_selectedInstruction = instruction;
		StateHasChanged();
	}

	void ToggleGroup(string key)
	{
		bool current = _expandedGroups.GetValueOrDefault(key, true);
		_expandedGroups[key] = !current;
	}
}
