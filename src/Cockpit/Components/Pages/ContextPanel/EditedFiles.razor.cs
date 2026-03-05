using Cockpit.Components.Popups;
using Cockpit.Features.Git.Models;
using Cockpit.Features.Sessions;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Pages.ContextPanel;

public partial class EditedFiles : ComponentBase, IDisposable
{
	readonly SessionListFeature _sessionListFeature;
	public EditedFiles(SessionListFeature sessionListFeature)
	{
		_sessionListFeature = sessionListFeature;
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

	EditedFilesPopup _diffPopup = default!;

	protected override void OnInitialized()
	{
		_sessionListFeature.OnStateChanged += OnStateChanged;
	}

	void OnStateChanged()
	{
		InvokeAsync(StateHasChanged);
	}

	void OpenDiffPopup(GitChangedFileModel? initialFile)
	{
		_diffPopup.Open(initialFile);
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