using System.Diagnostics;
using Cockpit.Components.Controls;
using Cockpit.Features.AppSettings;
using Cockpit.Features.Git.Models;
using Cockpit.Features.Sessions;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Popups;

public partial class EditedFilesPopup : ComponentBase, IDisposable
{
	readonly SessionListFeature _sessionListFeature;
	readonly IAppSettingsFeature _appSettingsFeature;
	public EditedFilesPopup(SessionListFeature sessionListFeature, IAppSettingsFeature appSettingsFeature)
	{
		_sessionListFeature = sessionListFeature;
		_appSettingsFeature = appSettingsFeature;
	}

	List<GitChangedFileModel> Files => _sessionListFeature.CurrentSession?.Context?.EditedFiles ?? [];

	PopupBase _popup = default!;
	GitChangedFileModel? _selectedFile;
	bool _splitView;

	string? _selectedFilePath =>
		_selectedFile is null ? null :
		_sessionListFeature.CurrentSession?.Context?.GitRoot is string root
			? Path.Combine(root, _selectedFile.Path.Replace('/', Path.DirectorySeparatorChar))
			: _selectedFile.Path;

	protected override void OnInitialized()
	{
		_splitView = _appSettingsFeature.DiffSplitView;
		_sessionListFeature.OnStateChanged += OnStateChanged;
	}

	public void Open(GitChangedFileModel? initialFile = null)
	{
		GitChangedFileModel? initial = initialFile ?? Files.FirstOrDefault();
		if(initial is not null)
		{
			SelectFile(initial);
		}
		_popup.Open();
	}

	void OnStateChanged()
	{
		if(_selectedFile is not null && !Files.Contains(_selectedFile))
		{
			GitChangedFileModel? refreshed = Files.FirstOrDefault(f => f.Path == _selectedFile.Path);
			_selectedFile = refreshed;
		}
		InvokeAsync(StateHasChanged);
	}

	void SelectFile(GitChangedFileModel file) => _selectedFile = file;

	void SetSplitView(bool split)
	{
		_splitView = split;
		_appSettingsFeature.DiffSplitView = split;
	}

	void RevealFile()
	{
		if(_selectedFilePath is null)
		{
			return;
		}

		try
		{
			if(OperatingSystem.IsWindows())
			{
				Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"/select,\"{_selectedFilePath}\"", UseShellExecute = true });
			}
			else if(OperatingSystem.IsMacOS())
			{
				Process.Start(new ProcessStartInfo { FileName = "open", Arguments = $"-R \"{_selectedFilePath}\"", UseShellExecute = false });
			}
		}
		catch { /* best-effort */ }
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
