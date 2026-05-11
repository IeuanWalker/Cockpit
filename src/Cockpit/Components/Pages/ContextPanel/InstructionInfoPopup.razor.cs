using Cockpit.Components.Controls;
using Cockpit.Utilities;
using GitHub.Copilot.SDK.Rpc;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Cockpit.Components.Pages.ContextPanel;

public sealed partial class InstructionInfoPopup : ComponentBase
{
	PopupBase? _popup;
	InstructionsSources? _selectedInstruction;
	List<InstructionsSources> _instructions = [];
	string? _workspacePath;
	bool _needsSplitInit;

	readonly IJSRuntime _jsRuntime;

	public InstructionInfoPopup(IJSRuntime jsRuntime)
	{
		_jsRuntime = jsRuntime;
	}

	public void Open(IReadOnlyList<InstructionsSources> instructions, InstructionsSources selectedInstruction, string? workspacePath = null)
	{
		_instructions = [.. instructions.OrderBy(i => i.Label, StringComparer.OrdinalIgnoreCase)];
		_workspacePath = workspacePath;
		_needsSplitInit = true;
		_popup?.Open();
		SelectInstruction(selectedInstruction);
	}

	void SelectInstruction(InstructionsSources instruction)
	{
		_selectedInstruction = instruction;
		StateHasChanged();
	}

	string ResolvedInstructionPath
	{
		get
		{
			string? path = _selectedInstruction?.SourcePath;
			if(string.IsNullOrEmpty(path))
			{
				return string.Empty;
			}

			if(!Path.IsPathRooted(path) && !string.IsNullOrEmpty(_workspacePath))
			{
				return Path.GetFullPath(Path.Combine(_workspacePath, path));
			}

			return path;
		}
	}

	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		if(_needsSplitInit)
		{
			_needsSplitInit = false;
			await _jsRuntime.InvokeVoidAsync("cockpit.initializePanelSplit", "instruction-left-panel", "instruction-split-handle");
		}
	}

	void RevealInstructionFile()
	{
		string path = ResolvedInstructionPath;
		if(!string.IsNullOrEmpty(path))
		{
			FileUtil.RevealFile(path);
		}
	}
}
