using System.Diagnostics;
using Cockpit.Features.Git;
using Cockpit.Features.Git.Models;
using Cockpit.Features.Sessions;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Popups;

public partial class GitDiffPopup : ComponentBase, IDisposable
{
	[Inject] SessionListFeature _sessionManager { get; set; } = default!;

	[Parameter] public bool IsOpen { get; set; }
	[Parameter] public GitChangedFileModel? InitialSelectedFile { get; set; }
	[Parameter] public EventCallback OnClose { get; set; }

	List<GitChangedFileModel> Files => _sessionManager.CurrentSession?.Context?.EditedFiles ?? [];

	GitChangedFileModel? _selectedFile;
	ParsedDiff? _parsedDiff;
	Dictionary<DiffHunk, List<SplitRow>> _splitRows = [];
	bool _splitView;
	bool _wasOpen;

	protected override void OnInitialized()
	{
		_sessionManager.OnStateChanged += OnStateChanged;
	}

	protected override void OnParametersSet()
	{
		if(IsOpen && !_wasOpen)
		{
			// Popup just opened — select initial file or first in list
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
		// If selected file is no longer in list, try to re-find by path
		if(_selectedFile is not null && !Files.Contains(_selectedFile))
		{
			GitChangedFileModel? refreshed = Files.FirstOrDefault(f => f.Path == _selectedFile.Path);
			if(refreshed is not null)
			{
				SelectFile(refreshed);
			}
			else
			{
				_selectedFile = null;
			}
		}

		InvokeAsync(StateHasChanged);
	}

	void SelectFile(GitChangedFileModel file)
	{
		_selectedFile = file;
		_parsedDiff = DiffParser.Parse(file.Diff);
		_splitRows = _parsedDiff?.Hunks.ToDictionary(h => h, DiffParser.BuildSplitRows) ?? [];
	}

	void SetSplitView(bool split) => _splitView = split;

	async Task Close() => await OnClose.InvokeAsync();

	void RevealFile()
	{
		if(_selectedFile is null)
		{
			return;
		}

		string? gitRoot = _sessionManager.CurrentSession?.Context.GitRoot;
		if(gitRoot is null)
		{
			return;
		}

		string fullPath = Path.Combine(gitRoot, _selectedFile.Path.Replace('/', Path.DirectorySeparatorChar));

		try
		{
			if(OperatingSystem.IsWindows())
			{
				Process.Start(new ProcessStartInfo
				{
					FileName = "explorer.exe",
					Arguments = $"/select,\"{fullPath}\"",
					UseShellExecute = true
				});
			}
			else if(OperatingSystem.IsMacOS())
			{
				Process.Start(new ProcessStartInfo
				{
					FileName = "open",
					Arguments = $"-R \"{fullPath}\"",
					UseShellExecute = false
				});
			}
		}
		catch
		{
			// Silently ignore — reveal is best-effort
		}
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
