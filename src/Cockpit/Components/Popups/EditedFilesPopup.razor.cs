using System.Diagnostics;
using Cockpit.Features.AppSettings;
using Cockpit.Features.Git.Models;
using Cockpit.Features.Sessions;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Popups;

public partial class EditedFilesPopup : ComponentBase, IDisposable
{
	[Inject] SessionListFeature _sessionManager { get; set; } = default!;
	[Inject] IAppSettingsFeature _appSettings { get; set; } = default!;

	[Parameter] public bool IsOpen { get; set; }
	[Parameter] public GitChangedFileModel? InitialSelectedFile { get; set; }
	[Parameter] public EventCallback OnClose { get; set; }

	List<GitChangedFileModel> Files => _sessionManager.CurrentSession?.Context?.EditedFiles ?? [];

	GitChangedFileModel? _selectedFile;
	bool _splitView;
	bool _wasOpen;

	string? _selectedFilePath =>
		_selectedFile is null ? null :
		_sessionManager.CurrentSession?.Context?.GitRoot is string root
			? Path.Combine(root, _selectedFile.Path.Replace('/', Path.DirectorySeparatorChar))
			: _selectedFile.Path;

	protected override void OnInitialized()
	{
		_splitView = _appSettings.DiffSplitView;
		_sessionManager.OnStateChanged += OnStateChanged;
	}

	protected override void OnParametersSet()
	{
		if(IsOpen && !_wasOpen)
		{
			GitChangedFileModel? initial = InitialSelectedFile ?? Files.FirstOrDefault();
			if(initial is not null)
			{
				SelectFile(initial);
			}
		}
		_wasOpen = IsOpen;
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
		_appSettings.DiffSplitView = split;
	}

	async Task Close() => await OnClose.InvokeAsync();

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
			_sessionManager.OnStateChanged -= OnStateChanged;
		}
	}
}
