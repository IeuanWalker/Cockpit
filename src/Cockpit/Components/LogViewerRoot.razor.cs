using System.Text;
using System.Text.RegularExpressions;
using Cockpit.Utilities.Logging;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Cockpit.Components;

public sealed partial class LogViewerRoot : ComponentBase, IAsyncDisposable
{
	[Inject] IJSRuntime JSRuntime { get; set; } = default!;

	readonly string _tableBodyId = $"lv-body-{Guid.NewGuid():N}";

	readonly List<LogTab> _tabs =
	[
		new("app.log",   "app.log",   Path.Combine(LogDirectoryHelper.LogDirectory, "app.log"),   IsBuiltIn: true),
		new("crash.log", "crash.log", Path.Combine(LogDirectoryHelper.LogDirectory, "crash.log"), IsBuiltIn: true),
	];
	LogTab _activeTab;

	List<LogEntry> _allEntries = [];
	List<LogEntry> _filtered = [];
	LogEntry? _selected;

	bool _autoScroll = true;
	bool _loading = true;
	bool _pendingScroll;
	bool _isScrolledUp;
	bool _scrollSetup;

	string _searchText = string.Empty;
	readonly HashSet<string> _hiddenLevels = [];

	long _knownFileLength = -1;
	CancellationTokenSource? _cts;
	Timer? _searchDebounce;
	DotNetObjectReference<LogViewerRoot>? _dotNetRef;

	public LogViewerRoot()
	{
		_activeTab = _tabs[0];
	}

	static readonly string[] levelOrder = ["Trace", "Debug", "Information", "Warning", "Error", "Critical"];

	static string LevelShort(string level) => level switch
	{
		"Trace" => "TRACE",
		"Debug" => "DEBUG",
		"Information" => "INFORMATION",
		"Warning" => "WARNING",
		"Error" => "ERROR",
		"Critical" => "CRITICAL",
		_ => level.ToUpperInvariant()
	};

	static string LevelClass(string level) => level.ToLowerInvariant() switch
	{
		"information" => "information",
		"warning" => "warning",
		"error" => "error",
		"critical" => "critical",
		"debug" => "debug",
		"trace" => "trace",
		_ => "raw"
	};

	int GetLevelCount(string level) => _allEntries.Count(e => e.LevelNorm == level);

	static List<LogEntry> Parse(string raw)
	{
		List<LogEntry> entries = [];
		int idx = 0;
		LogEntry? current = null;
		StringBuilder detailBuf = new();

		foreach(string rawLine in raw.Split('\n'))
		{
			string line = rawLine.TrimEnd('\r');

			// Try structured app.log format first
			Match m = EntryPattern().Match(line);
			if(m.Success)
			{
				if(current is not null)
				{
					current.Detail = detailBuf.ToString().TrimEnd();
					entries.Add(current);
				}
				detailBuf.Clear();
				detailBuf.AppendLine(line);
				current = new LogEntry
				{
					Index = idx++,
					Timestamp = DateTime.TryParse(m.Groups[1].Value, out DateTime dt) ? dt : null,
					Level = m.Groups[2].Value,
					Category = m.Groups[3].Value.Trim(),
					Message = m.Groups[4].Value,
				};
				continue;
			}

			// Try crash.log header format
			Match cm = CrashHeaderPattern().Match(line);
			if(cm.Success)
			{
				if(current is not null)
				{
					current.Detail = detailBuf.ToString().TrimEnd();
					entries.Add(current);
				}
				detailBuf.Clear();
				current = new LogEntry
				{
					Index = idx++,
					Timestamp = DateTime.TryParse(cm.Groups[1].Value, out DateTime cdt) ? cdt : null,
					Level = "Critical",
					Category = cm.Groups[2].Value.Trim(),
					Message = string.Empty, // filled in by first body line below
				};
				continue;
			}

			if(line.Length == 0)
			{
				continue;
			}

			if(current is not null)
			{
				// First non-empty body line after a crash header becomes the Message
				if(current.Message.Length == 0 && detailBuf.Length == 0)
				{
					current.Message = line;
				}

				detailBuf.AppendLine(line);
			}
			else
			{
				entries.Add(new LogEntry
				{
					Index = idx++,
					Level = "Raw",
					Message = line,
					Detail = line
				});
			}
		}

		if(current is not null)
		{
			current.Detail = detailBuf.ToString().TrimEnd();
			entries.Add(current);
		}

		return entries;
	}

	protected override void OnInitialized()
	{
		_ = LoadAsync();
		StartPolling();
	}

	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		if(firstRender)
		{
			_dotNetRef = DotNetObjectReference.Create(this);
		}

		if(!_scrollSetup && !_loading)
		{
			_scrollSetup = true;
			try
			{
				await JSRuntime.InvokeVoidAsync("cockpit.setupLogViewerScroll", _tableBodyId, _dotNetRef, nameof(OnScrollPositionChanged));
			}
			catch { /* best-effort */ }
		}

		if(_pendingScroll && _autoScroll && !_isScrolledUp)
		{
			_pendingScroll = false;
			try
			{
				await JSRuntime.InvokeVoidAsync("cockpit.scrollToBottom", _tableBodyId);
			}
			catch { /* best-effort */ }
		}
	}

	[JSInvokable]
	public void OnScrollPositionChanged(bool isNearBottom)
	{
		_isScrolledUp = !isNearBottom;
		InvokeAsync(StateHasChanged);
	}

	async Task LoadAsync()
	{
		_loading = true;
		(string? content, long len) = await Task.Run(() => ReadFile(_activeTab.FilePath));
		_knownFileLength = len;
		_allEntries = Parse(content);
		RebuildFiltered();
		_loading = false;
		_pendingScroll = _autoScroll;
		await InvokeAsync(StateHasChanged);
	}

	static (string content, long length) ReadFile(string path)
	{
		try
		{
			if(!File.Exists(path))
			{
				return (string.Empty, 0);
			}

			using FileStream fs = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
			using StreamReader sr = new(fs);
			return (sr.ReadToEnd(), fs.Length);
		}
		catch
		{
			return (string.Empty, 0);
		}
	}

	void StartPolling()
	{
		_cts?.Cancel();
		_cts?.Dispose();
		_cts = new CancellationTokenSource();
		CancellationToken token = _cts.Token;

		_ = Task.Run(async () =>
		{
			using PeriodicTimer timer = new(TimeSpan.FromSeconds(1));
			while(!token.IsCancellationRequested && await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
			{
				string path = _activeTab.FilePath;
				long len = 0;
				try
				{
					len = File.Exists(path) ? new FileInfo(path).Length : 0;
				}
				catch
				{
					continue;
				}

				if(len == _knownFileLength)
				{
					continue;
				}

				(string? content, long newLen) = ReadFile(path);
				_knownFileLength = newLen;
				_allEntries = Parse(content);

				await InvokeAsync(() =>
				{
					RebuildFiltered();
					if(_autoScroll && !_isScrolledUp)
					{
						_pendingScroll = true;
					}

					StateHasChanged();
				}).ConfigureAwait(false);
			}
		}, token);
	}

	void RebuildFiltered()
	{
		string search = _searchText.Trim();
		_filtered = [.. _allEntries.Where(e =>
		{
			if(_hiddenLevels.Contains(e.LevelNorm))
			{
				return false;
			}

			if(search.Length == 0)
			{
				return true;
			}

			return e.TimestampDisplay.Contains(search, StringComparison.OrdinalIgnoreCase)
				|| e.LevelNorm.Contains(search, StringComparison.OrdinalIgnoreCase)
				|| e.Category.Contains(search, StringComparison.OrdinalIgnoreCase)
				|| e.Message.Contains(search, StringComparison.OrdinalIgnoreCase)
				|| e.Detail.Contains(search, StringComparison.OrdinalIgnoreCase);
		})];
	}

	void SwitchTab(LogTab tab)
	{
		if(tab.Id == _activeTab.Id)
		{
			return;
		}

		_activeTab = tab;
		_knownFileLength = -1;
		_selected = null;
		_isScrolledUp = false;
		_scrollSetup = false;
		_loading = true;
		StateHasChanged();
		_ = Task.Run(async () =>
		{
			await CleanupScrollAsync();
			await InvokeAsync(async () =>
			{
				await LoadAsync();
			});
		});
	}

	void CloseTab(LogTab tab)
	{
		int idx = _tabs.IndexOf(tab);
		_tabs.Remove(tab);
		if(_activeTab.Id == tab.Id)
		{
			SwitchTab(_tabs[Math.Max(0, idx - 1)]);
		}

		StateHasChanged();
	}

	async Task OpenFileAsync()
	{
		try
		{
			FileResult? result = await MainThread.InvokeOnMainThreadAsync(() =>
				FilePicker.Default.PickAsync(new PickOptions
				{
					PickerTitle = "Open Log File",
					FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
					{
						{ DevicePlatform.WinUI,        new[] { ".log", ".txt", "*" } },
						{ DevicePlatform.MacCatalyst,  new[] { "public.plain-text" } },
						{ DevicePlatform.macOS,        new[] { "public.plain-text" } },
					})
				}));

			if(result is null)
			{
				return;
			}

			string id = Guid.NewGuid().ToString("N");
			LogTab tab = new(id, result.FileName, result.FullPath, IsBuiltIn: false);
			_tabs.Add(tab);
			SwitchTab(tab);
		}
		catch { /* best-effort */ }
	}

	void RefreshNow()
	{
		_knownFileLength = -1;
		_ = LoadAsync();
	}

	void ToggleLevel(string level)
	{
		if(!_hiddenLevels.Remove(level))
		{
			_hiddenLevels.Add(level);
		}

		RebuildFiltered();
	}

	void SelectEntry(LogEntry entry) => _selected = _selected?.Index == entry.Index ? null : entry;

	void OnSearchInput(ChangeEventArgs e)
	{
		_searchText = e.Value?.ToString() ?? string.Empty;
		_searchDebounce?.Dispose();
		_searchDebounce = new Timer(_ =>
			InvokeAsync(() =>
			{
				RebuildFiltered();
				StateHasChanged();
			}),
			null, 250, Timeout.Infinite);
	}

	void ClearSearch()
	{
		_searchText = string.Empty;
		_searchDebounce?.Dispose();
		RebuildFiltered();
	}

	async Task ScrollToBottomAndResume()
	{
		_isScrolledUp = false;
		try
		{
			await JSRuntime.InvokeVoidAsync("cockpit.scrollToBottom", _tableBodyId);
		}
		catch { /* best-effort */ }
	}

	async Task CleanupScrollAsync()
	{
		try
		{
			await JSRuntime.InvokeVoidAsync("cockpit.cleanupLogViewerScroll", _tableBodyId);
		}
		catch { /* best-effort */ }
	}

	public async ValueTask DisposeAsync()
	{
		_cts?.Cancel();
		_cts?.Dispose();
		_cts = null;
		_searchDebounce?.Dispose();
		_searchDebounce = null;
		await CleanupScrollAsync();
		_dotNetRef?.Dispose();
		_dotNetRef = null;
	}

	[GeneratedRegex(@"^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}) \[([^\]]+)\] ([^:]+): (.*)", RegexOptions.Compiled)]
	private static partial Regex EntryPattern();
	[GeneratedRegex(@"^=== (\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}) \[([^\]]+)\] ===$", RegexOptions.Compiled)]
	private static partial Regex CrashHeaderPattern(); // Matches crash.log headers:  === 2026-04-05 14:16:51 [TaskScheduler] ===
}

sealed record LogTab(string Id, string Label, string FilePath, bool IsBuiltIn = false);

sealed class LogEntry
{
	public int Index;
	public DateTime? Timestamp;
	public string Level = string.Empty;
	public string Category = string.Empty;
	public string Message = string.Empty;
	public string Detail = string.Empty;

	public string LevelNorm => Level.Trim();

	public string CategoryShort
	{
		get
		{
			int dot = Category.LastIndexOf('.');
			return dot >= 0 ? Category[(dot + 1)..] : Category;
		}
	}

	public string TimestampDisplay => Timestamp?.ToString("yyyy-MM-dd HH:mm:ss.fff") ?? string.Empty;

	/// <summary>Everything after the first line of Detail (exception / stack trace).</summary>
	public string Continuation
	{
		get
		{
			int nl = Detail.IndexOf('\n');
			if(nl < 0 || nl >= Detail.Length - 1)
			{
				return string.Empty;
			}

			return Detail[(nl + 1)..].Trim();
		}
	}
}
