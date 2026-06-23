using Cockpit.Features.FileSearch;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Cockpit.Components.Pages.ChatPanel.InputArea;

public sealed partial class FileMentionPicker : ComponentBase
{
	readonly IJSRuntime _jsRuntime;

	public FileMentionPicker(IJSRuntime jsRuntime)
	{
		_jsRuntime = jsRuntime;
	}

	[Parameter] public bool Visible { get; set; }
	[Parameter] public string Filter { get; set; } = string.Empty;
	[Parameter] public IReadOnlyList<FileSearchResult> Files { get; set; } = [];
	[Parameter] public IReadOnlyCollection<string> AttachedPaths { get; set; } = [];
	[Parameter] public EventCallback<FileSearchResult> OnFileSelected { get; set; }
	[Parameter] public EventCallback OnClose { get; set; }
	[Parameter] public int SelectedIndex { get; set; }
	[Parameter] public EventCallback<int> SelectedIndexChanged { get; set; }

	readonly List<(FileSearchResult File, bool IsAttached)> _orderedFiles = [];

	int _lastScrolledIndex = -2;

	protected override void OnParametersSet()
	{
		if(!Visible)
		{
			_lastScrolledIndex = -2;
		}

		_orderedFiles.Clear();
		foreach(FileSearchResult f in Files)
		{
			if(AttachedPaths.Contains(f.FullPath))
			{
				_orderedFiles.Add((f, true));
			}
		}
		foreach(FileSearchResult f in Files)
		{
			if(!AttachedPaths.Contains(f.FullPath))
			{
				_orderedFiles.Add((f, false));
			}
		}
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
			catch(Exception)
			{
				// Swallow - picker element may not yet be in the DOM, or component is mid-disposal
			}
		}
	}

	static string GetDisplayPath(FileSearchResult file) =>
		Path.GetDirectoryName(file.RelativePath) ?? string.Empty;

	Task SelectFile(FileSearchResult file) => OnFileSelected.InvokeAsync(file);
}
