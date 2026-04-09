using Cockpit.Features.Git;
using Cockpit.Features.Git.Models;
using Cockpit.Features.Sessions;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Pages.ContextPanel;

public partial class EditedFiles : ComponentBase, IDisposable
{
	readonly SessionListFeature _sessionListFeature;
	readonly EditedFilesWindowService _windowService;

	public EditedFiles(SessionListFeature sessionListFeature, EditedFilesWindowService windowService)
	{
		_sessionListFeature = sessionListFeature;
		_windowService = windowService;
	}

	List<GitChangedFileModel> Files => _sessionListFeature.CurrentSession?.Context?.EditedFiles ?? [];

	List<GitChangedFileModel> _renderedFiles = [];
	int _renderedFilesCount = -1;

	protected override bool ShouldRender()
	{
		List<GitChangedFileModel> current = Files;
		int currentCount = current.Count;
		if(ReferenceEquals(current, _renderedFiles) && currentCount == _renderedFilesCount)
		{
			return false;
		}

		_renderedFiles = current;
		_renderedFilesCount = currentCount;
		return true;
	}

	protected override void OnInitialized()
	{
		_sessionListFeature.OnStateChanged += OnStateChanged;
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
		Dispose(true);
		GC.SuppressFinalize(this);
	}

	protected virtual void Dispose(bool disposing)
	{
		if(disposing)
		{
			_sessionListFeature.OnStateChanged -= OnStateChanged;
		}
	}
}