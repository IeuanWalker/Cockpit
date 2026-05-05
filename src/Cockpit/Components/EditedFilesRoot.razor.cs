using Cockpit.Components.Controls.GitDiff;
using Cockpit.Features.AppSettings;
using Cockpit.Features.Git;
using Cockpit.Features.Git.Models;
using Cockpit.Features.Sessions;
using Cockpit.Features.Splash;
using Cockpit.Features.Theme;
using Cockpit.Utilities;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Cockpit.Components;

public sealed partial class EditedFilesRoot : ComponentBase, IAsyncDisposable
{
	readonly IJSRuntime _jsRuntime;
	readonly ThemeStateFeature _themeStateFeature;
	readonly SessionListFeature _sessionListFeature;
	readonly IAppSettingsFeature _appSettingsFeature;
	readonly EditedFilesWindowService _windowService;
	readonly EditedFilesSplashFeature _splashFeature;

	public EditedFilesRoot(
		IJSRuntime jsRuntime,
		ThemeStateFeature themeStateFeature,
		SessionListFeature sessionListFeature,
		IAppSettingsFeature appSettingsFeature,
		EditedFilesWindowService windowService,
		EditedFilesSplashFeature splashFeature)
	{
		_jsRuntime = jsRuntime;
		_themeStateFeature = themeStateFeature;
		_sessionListFeature = sessionListFeature;
		_appSettingsFeature = appSettingsFeature;
		_windowService = windowService;
		_splashFeature = splashFeature;
	}

	List<GitChangedFileModel> Files => _sessionListFeature.CurrentSession?.Context?.EditedFiles ?? [];

	GitChangedFileModel? _selectedFile;
	GitDiffViewer? _diffViewer;
	bool _splitView;
	bool _treeView;
	readonly Dictionary<string, bool> _expandedDirs = new(StringComparer.OrdinalIgnoreCase);
	List<DisplayNode>? _cachedNodes;

	List<DisplayNode> DisplayNodes => _cachedNodes ??= BuildDisplayNodes();

	string? _selectedFilePath
	{
		get
		{
			if(_selectedFile is null)
			{
				return null;
			}

			if(_sessionListFeature.CurrentSession?.Context?.GitRoot is string root)
			{
				return Path.Combine(root, _selectedFile.Path.Replace('/', Path.DirectorySeparatorChar));
			}

			return _selectedFile.Path;
		}
	}

	protected override void OnInitialized()
	{
		_splitView = _appSettingsFeature.DiffSplitView;
		_treeView = _appSettingsFeature.DiffTreeView;
		_sessionListFeature.OnStateChanged += OnStateChanged;
		_windowService.OnNavigateToFile += OnNavigateToFile;

		GitChangedFileModel? initial = _windowService.PendingInitialFile ?? Files.FirstOrDefault();
		_windowService.ConsumePendingInitialFile();
		if(initial is not null)
		{
			SelectFile(initial);
		}
	}

	void OnNavigateToFile(GitChangedFileModel file)
	{
		InvokeAsync(() =>
		{
			SelectFile(file);
			StateHasChanged();
		});
	}

	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		if(firstRender)
		{
			await ApplyThemeAsync();
			_themeStateFeature.OnThemeChanged += OnThemeChangedHandler;
			_splashFeature.NotifyBlazorReady();
		}
	}

	void OnStateChanged()
	{
		if(_selectedFile is not null && !Files.Contains(_selectedFile))
		{
			GitChangedFileModel? refreshed = Files.FirstOrDefault(f => f.Path == _selectedFile.Path);
			_selectedFile = refreshed;
		}

		_cachedNodes = null;
		InvokeAsync(StateHasChanged);
	}

	void SelectFile(GitChangedFileModel file) => _selectedFile = file;

	void SetSplitView(bool split)
	{
		_splitView = split;
		_appSettingsFeature.DiffSplitView = split;
	}

	void SetTreeView(bool tree)
	{
		_treeView = tree;
		_appSettingsFeature.DiffTreeView = tree;
		_cachedNodes = null;
	}

	void ToggleDir(string key)
	{
		bool current = _expandedDirs.GetValueOrDefault(key, true);
		_expandedDirs[key] = !current;
		_cachedNodes = null;
	}

	void RevealFile()
	{
		FileUtil.RevealFile(_selectedFilePath);
	}

	async Task ExpandAllLines()
	{
		if(_diffViewer is not null)
		{
			await _diffViewer.ExpandAllAsync();
		}
	}

	List<DisplayNode> BuildDisplayNodes()
	{
		if(!_treeView)
		{
			return [.. Files.Select(f => new DisplayNode(f.Name, 0, false, null, f))];
		}

		FileTreeDir root = new() { Name = string.Empty, Key = string.Empty };

		foreach(GitChangedFileModel file in Files)
		{
			string[] parts = file.Path.Split('/');
			FileTreeDir dir = root;

			for(int i = 0; i < parts.Length - 1; i++)
			{
				string part = parts[i];
				string key = i == 0 ? part : dir.Key + "/" + part;

				if(!dir.Dirs.TryGetValue(part, out FileTreeDir? child))
				{
					child = new FileTreeDir { Name = part, Key = key };
					dir.Dirs[part] = child;
				}

				dir = child;
			}

			dir.Files.Add(file);
		}

		List<DisplayNode> result = [];
		AppendDirNodes(root, 0, result);
		return result;
	}

	void AppendDirNodes(FileTreeDir dir, int depth, List<DisplayNode> result)
	{
		foreach(FileTreeDir subDir in dir.Dirs.Values.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
		{
			result.Add(new DisplayNode(subDir.Name, depth, true, subDir.Key, null));

			if(_expandedDirs.GetValueOrDefault(subDir.Key, true))
			{
				AppendDirNodes(subDir, depth + 1, result);
			}
		}

		foreach(GitChangedFileModel file in dir.Files.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
		{
			result.Add(new DisplayNode(file.Name, depth, false, null, file));
		}
	}

	void OnThemeChangedHandler() => _ = ApplyThemeAsync();

	async Task ApplyThemeAsync()
	{
		try
		{
			if(_themeStateFeature.IsLightTheme)
			{
				await _jsRuntime.InvokeVoidAsync("cockpit.addBodyClass", "light-theme");
			}
			else
			{
				await _jsRuntime.InvokeVoidAsync("cockpit.removeBodyClass", "light-theme");
			}

			await _jsRuntime.InvokeVoidAsync("cockpit.setAccentColor", _themeStateFeature.AccentColor, _themeStateFeature.AccentHoverColor);
		}
		catch { /* best-effort */ }
	}

	public async ValueTask DisposeAsync()
	{
		_sessionListFeature.OnStateChanged -= OnStateChanged;
		_themeStateFeature.OnThemeChanged -= OnThemeChangedHandler;
		_windowService.OnNavigateToFile -= OnNavigateToFile;
	}

	sealed record DisplayNode(string Name, int Depth, bool IsDirectory, string? DirKey, GitChangedFileModel? File);

	sealed class FileTreeDir
	{
		public required string Name { get; init; }
		public required string Key { get; init; }
		public Dictionary<string, FileTreeDir> Dirs { get; } = new(StringComparer.OrdinalIgnoreCase);
		public List<GitChangedFileModel> Files { get; } = [];
	}
}
