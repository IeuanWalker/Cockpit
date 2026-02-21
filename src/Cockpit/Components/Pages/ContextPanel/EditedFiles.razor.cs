using Cockpit.Features.Git.Models;
using Cockpit.Features.Sessions;
using Cockpit.Features.UIState;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Pages.ContextPanel;

public partial class EditedFiles : ComponentBase, IDisposable
{
	[Inject] UIStateFeature _uiState { get; set; } = null!;
	[Inject] SessionListFeature _sessionManager { get; set; } = default!;

	List<GitChangedFileModel> Files => _sessionManager.CurrentSession?.Context?.EditedFiles ?? [];

	bool _isDiffPopupOpen;
	GitChangedFileModel? _diffPopupInitialFile;

	protected override void OnInitialized()
	{
		_sessionManager.OnStateChanged += OnStateChanged;
	}

	void OnStateChanged()
	{
		InvokeAsync(StateHasChanged);
	}

	void OpenDiffPopup(GitChangedFileModel? initialFile)
	{
		_diffPopupInitialFile = initialFile;
		_isDiffPopupOpen = true;
	}

	void CloseDiffPopup()
	{
		_isDiffPopupOpen = false;
		_diffPopupInitialFile = null;
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
			_sessionManager.OnStateChanged -= OnStateChanged;
		}
	}
}