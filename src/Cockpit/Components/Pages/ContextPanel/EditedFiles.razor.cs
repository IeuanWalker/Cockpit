using Cockpit.Features.Git;
using Cockpit.Features.Git.Models;
using Cockpit.Features.Sessions;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Pages.ContextPanel;

public sealed partial class EditedFiles : ComponentBase, IDisposable
{
	readonly SessionListFeature _sessionListFeature;
	readonly EditedFilesWindowService _windowService;

	public EditedFiles(SessionListFeature sessionListFeature, EditedFilesWindowService windowService)
	{
		_sessionListFeature = sessionListFeature;
		_windowService = windowService;
	}

	List<GitChangedFileModel> Files => _sessionListFeature.CurrentSession?.Context?.EditedFiles ?? [];

	// Track the raw list reference (before ??) to avoid spurious re-renders when the
	// property getter creates a new empty list for a null EditedFiles each call.
	List<GitChangedFileModel>? _renderedFilesList;
	int _renderedFilesCount = -1;

	protected override bool ShouldRender()
	{
		List<GitChangedFileModel>? currentList = _sessionListFeature.CurrentSession?.Context?.EditedFiles;
		int currentCount = currentList?.Count ?? 0;

		if(ReferenceEquals(currentList, _renderedFilesList) && currentCount == _renderedFilesCount)
		{
			return false;
		}

		_renderedFilesList = currentList;
		_renderedFilesCount = currentCount;
		return true;
	}

	protected override void OnInitialized()
	{
		_sessionListFeature.OnStateChanged += OnStateChanged;
		// Seed the sentinel so the first StateHasChanged after initial render doesn't
		// unconditionally re-render when nothing has actually changed.
		List<GitChangedFileModel>? list = _sessionListFeature.CurrentSession?.Context?.EditedFiles;
		_renderedFilesList = list;
		_renderedFilesCount = list?.Count ?? 0;
	}

	void OnStateChanged()
	{
		InvokeAsync(StateHasChanged);
	}

	void OpenDiffWindow(GitChangedFileModel? initialFile)
	{
		_windowService.OpenWindow(initialFile);
	}

	public void Dispose()
	{
		_sessionListFeature.OnStateChanged -= OnStateChanged;
	}
}