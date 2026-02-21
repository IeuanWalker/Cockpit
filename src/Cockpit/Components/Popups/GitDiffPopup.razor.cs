using System.Diagnostics;
using Cockpit.Features.AppSettings;
using Cockpit.Features.Git;
using Cockpit.Features.Git.Models;
using Cockpit.Features.Sessions;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Cockpit.Components.Popups;

public partial class GitDiffPopup : ComponentBase, IDisposable
{
	[Inject] SessionListFeature _sessionManager { get; set; } = default!;
	[Inject] IAppSettingsFeature _appSettings { get; set; } = default!;
	[Inject] IJSRuntime JS { get; set; } = default!;

	[Parameter] public bool IsOpen { get; set; }
	[Parameter] public GitChangedFileModel? InitialSelectedFile { get; set; }
	[Parameter] public EventCallback OnClose { get; set; }

	List<GitChangedFileModel> Files => _sessionManager.CurrentSession?.Context?.EditedFiles ?? [];

	GitChangedFileModel? _selectedFile;
	ParsedDiff? _parsedDiff;
	Dictionary<DiffHunk, List<SplitRow>> _splitRows = [];
	bool _splitView;
	bool _wasOpen;
	bool _needsHighlight;

	readonly string _diffInlineId = $"diff-inline-{Guid.NewGuid():N}";
	readonly string _diffLeftId   = $"diff-left-{Guid.NewGuid():N}";
	readonly string _diffRightId  = $"diff-right-{Guid.NewGuid():N}";

	// Computed display properties for selected file — precomputed to avoid inline vars in razor
	string SelectedStatusLabel => _selectedFile?.Status switch
	{
		GitFileStatus.Modified  => "Modified",
		GitFileStatus.Added     => "Added",
		GitFileStatus.Deleted   => "Deleted",
		GitFileStatus.Renamed   => "Renamed",
		_                       => "Untracked"
	} ?? string.Empty;

	string SelectedStatusClass => _selectedFile?.Status switch
	{
		GitFileStatus.Modified  => "bg-yellow-600",
		GitFileStatus.Added     => "bg-green-600",
		GitFileStatus.Deleted   => "bg-red-600",
		GitFileStatus.Renamed   => "bg-blue-600",
		_                       => "bg-gray-600"
	} ?? string.Empty;

	static readonly Dictionary<string, string> _extensionLanguageMap = new(StringComparer.OrdinalIgnoreCase)
	{
		[".cs"]    = "csharp",
		[".js"]    = "javascript",
		[".ts"]    = "typescript",
		[".tsx"]   = "typescript",
		[".jsx"]   = "javascript",
		[".py"]    = "python",
		[".java"]  = "java",
		[".json"]  = "json",
		[".xml"]   = "xml",
		[".html"]  = "html",
		[".htm"]   = "html",
		[".css"]   = "css",
		[".scss"]  = "css",
		[".sh"]    = "bash",
		[".bash"]  = "bash",
		[".sql"]   = "sql",
		[".md"]    = "markdown",
		[".yaml"]  = "yaml",
		[".yml"]   = "yaml",
		[".razor"] = "html",
		[".go"]    = "go",
		[".rs"]    = "rust",
		[".cpp"]   = "cpp",
		[".c"]     = "c",
		[".h"]     = "cpp",
		[".php"]   = "php",
		[".rb"]    = "ruby",
		[".swift"] = "swift",
		[".kt"]    = "kotlin",
	};

	string DetectLanguage(string? filePath)
	{
		if(string.IsNullOrEmpty(filePath)) return "plaintext";
		string ext = Path.GetExtension(filePath);
		return _extensionLanguageMap.TryGetValue(ext, out string? lang) ? lang : "plaintext";
	}

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
				SelectFile(initial);
		}
		_wasOpen = IsOpen;
	}

	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		if(_needsHighlight && IsOpen)
		{
			_needsHighlight = false;
			string language = DetectLanguage(_selectedFile?.Path);
			try
			{
				if(_splitView)
				{
					await JS.InvokeVoidAsync("cockpit.highlightDiffCells", _diffLeftId, language);
					await JS.InvokeVoidAsync("cockpit.highlightDiffCells", _diffRightId, language);
					await JS.InvokeVoidAsync("cockpit.setupSplitDiffScroll", _diffLeftId, _diffRightId);
				}
				else
				{
					await JS.InvokeVoidAsync("cockpit.highlightDiffCells", _diffInlineId, language);
				}
			}
			catch { /* ignore if hljs unavailable */ }
		}
	}

	void OnStateChanged()
	{
		if(_selectedFile is not null && !Files.Contains(_selectedFile))
		{
			GitChangedFileModel? refreshed = Files.FirstOrDefault(f => f.Path == _selectedFile.Path);
			if(refreshed is not null)
				SelectFile(refreshed);
			else
				_selectedFile = null;
		}

		InvokeAsync(StateHasChanged);
	}

	void SelectFile(GitChangedFileModel file)
	{
		_selectedFile = file;
		_parsedDiff = DiffParser.Parse(file.Diff);
		_splitRows = _parsedDiff?.Hunks.ToDictionary(h => h, DiffParser.BuildSplitRows) ?? [];
		_needsHighlight = true;
	}

	void SetSplitView(bool split)
	{
		_splitView = split;
		_appSettings.DiffSplitView = split;
		_needsHighlight = true;
	}

	async Task Close() => await OnClose.InvokeAsync();

	void RevealFile()
	{
		if(_selectedFile is null) return;

		string? gitRoot = _sessionManager.CurrentSession?.Context.GitRoot;
		if(gitRoot is null) return;

		string fullPath = Path.Combine(gitRoot, _selectedFile.Path.Replace('/', Path.DirectorySeparatorChar));

		try
		{
			if(OperatingSystem.IsWindows())
				Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = $"/select,\"{fullPath}\"", UseShellExecute = true });
			else if(OperatingSystem.IsMacOS())
				Process.Start(new ProcessStartInfo { FileName = "open", Arguments = $"-R \"{fullPath}\"", UseShellExecute = false });
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
			_sessionManager.OnStateChanged -= OnStateChanged;
	}
}
