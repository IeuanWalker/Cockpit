using Cockpit.Features.FileSearch;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Cockpit.Components.Pages.ChatPanel.InputArea;

public partial class FileMentionPicker : ComponentBase
{
	readonly IJSRuntime _jsRuntime;

	public FileMentionPicker(IJSRuntime jsRuntime)
	{
		_jsRuntime = jsRuntime;
	}

	[Parameter] public bool Visible { get; set; }
	[Parameter] public string Filter { get; set; } = string.Empty;
	[Parameter] public IReadOnlyList<FileSearchResult> Files { get; set; } = [];
	[Parameter] public EventCallback<FileSearchResult> OnFileSelected { get; set; }
	[Parameter] public EventCallback OnClose { get; set; }
	[Parameter] public int SelectedIndex { get; set; }
	[Parameter] public EventCallback<int> SelectedIndexChanged { get; set; }

	IReadOnlyList<FileSearchResult> _filteredFiles = [];
	int _lastScrolledIndex = -2; // -2 = never scrolled

	protected override void OnParametersSet()
	{
		_filteredFiles = Files;
	}

	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		if(Visible && SelectedIndex >= 0 && SelectedIndex != _lastScrolledIndex)
		{
			_lastScrolledIndex = SelectedIndex;
			try
			{
				await _jsRuntime.InvokeVoidAsync("cockpit.scrollPickerItemIntoView", $"picker-item-{SelectedIndex}");
			}
			catch { /* picker may not be in DOM yet */ }
		}
	}

	static string GetDisplayPath(FileSearchResult file)
	{
		string dir = Path.GetDirectoryName(file.RelativePath) ?? string.Empty;
		return string.IsNullOrEmpty(dir) ? string.Empty : dir;
	}

	async Task SelectFile(FileSearchResult file)
	{
		await OnFileSelected.InvokeAsync(file);
	}
}
