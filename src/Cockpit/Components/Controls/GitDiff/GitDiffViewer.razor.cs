using Cockpit.Components.Controls.GitDiff.Models;
using Cockpit.Features.Git.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Cockpit.Components.Controls.GitDiff;

public partial class GitDiffViewer : ComponentBase
{
	enum FileCategory { Text, Image, Video, Binary }

	[Parameter] public string? Diff { get; set; }
	[Parameter] public string? FilePath { get; set; }
	[Parameter] public bool SplitView { get; set; }
	[Parameter] public GitFileStatusEnum? GitFileStatus { get; set; }

	readonly IJSRuntime _jsRuntime;
	public GitDiffViewer(IJSRuntime jsRuntime)
	{
		_jsRuntime = jsRuntime;
	}

	ParsedDiffModel? _parsedDiff;
	Dictionary<DiffHunkModel, List<SplitRowModel>> _splitRows = [];
	Dictionary<DiffLineModel, List<(int Start, int Length)>> _inlineSpans = [];
	string[]? _fileLines;
	readonly Dictionary<DiffHunkModel, (int Above, int Below)> _hunkExpansion = [];
	bool _needsHighlight;

	FileCategory _fileCategory;
	string? _mediaDataUrl;
	bool _mediaLoading;
	bool _mediaTooBig;

	string? _prevDiff;
	string? _prevFilePath;
	bool _prevSplitView;

	readonly string _diffInlineId = $"diff-inline-{Guid.NewGuid():N}";
	readonly string _diffSplitLeftId = $"diff-split-left-{Guid.NewGuid():N}";
	readonly string _diffSplitRightId = $"diff-split-right-{Guid.NewGuid():N}";

	string FileName => string.IsNullOrEmpty(FilePath) ? string.Empty : Path.GetFileName(FilePath);

	static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
	{
		".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".svg", ".webp", ".tiff", ".tif"
	};

	static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
	{
		".mp4", ".avi", ".mov", ".wmv", ".flv", ".webm", ".mkv", ".m4v", ".ogv", ".ogg"
	};

	static readonly HashSet<string> BinaryExtensions = new(StringComparer.OrdinalIgnoreCase)
	{
		".dll", ".exe", ".pdb", ".obj", ".lib", ".bin", ".zip", ".rar", ".7z",
		".tar", ".gz", ".bz2", ".xz", ".jar", ".war", ".ear", ".class",
		".so", ".dylib", ".a", ".o", ".pdf", ".doc", ".docx", ".xls", ".xlsx",
		".ppt", ".pptx", ".nupkg", ".wasm", ".db", ".sqlite", ".mdb",
		".ttf", ".otf", ".woff", ".woff2", ".eot", ".pyc", ".pyo"
	};

	static FileCategory GetFileCategory(string? filePath)
	{
		if(string.IsNullOrEmpty(filePath))
		{
			return FileCategory.Text;
		}

		string ext = Path.GetExtension(filePath);
		if(ImageExtensions.Contains(ext)) { return FileCategory.Image; }
		if(VideoExtensions.Contains(ext)) { return FileCategory.Video; }
		if(BinaryExtensions.Contains(ext)) { return FileCategory.Binary; }
		return FileCategory.Text;
	}

	static readonly Dictionary<string, string> extensionLanguageMap = new(StringComparer.OrdinalIgnoreCase)
	{
		[".cs"] = "csharp",
		[".js"] = "javascript",
		[".ts"] = "typescript",
		[".tsx"] = "typescript",
		[".jsx"] = "javascript",
		[".py"] = "python",
		[".java"] = "java",
		[".json"] = "json",
		[".xml"] = "xml",
		[".html"] = "html",
		[".htm"] = "html",
		[".css"] = "css",
		[".scss"] = "scss",
		[".sh"] = "bash",
		[".bash"] = "bash",
		[".sql"] = "sql",
		[".md"] = "markdown",
		[".yaml"] = "yaml",
		[".yml"] = "yaml",
		[".razor"] = "html",
		[".go"] = "go",
		[".rs"] = "rust",
		[".cpp"] = "cpp",
		[".c"] = "cpp",
		[".h"] = "cpp",
		[".php"] = "php",
		[".rb"] = "ruby",
		[".swift"] = "swift",
		[".kt"] = "kotlin",
	};

	static string DetectLanguage(string? filePath) =>
		string.IsNullOrEmpty(filePath) ? "plaintext" :
		extensionLanguageMap.TryGetValue(Path.GetExtension(filePath), out string? lang) ? lang : "plaintext";

	string DetectLanguage() => DetectLanguage(FilePath);

	static Dictionary<DiffLineModel, List<(int Start, int Length)>> ComputeInlineSpans(ParsedDiffModel? diff)
	{
		Dictionary<DiffLineModel, List<(int Start, int Length)>> result = [];
		if(diff is null)
		{
			return result;
		}

		foreach(DiffHunkModel hunk in diff.Hunks)
		{
			List<DiffLineModel> lines = hunk.Lines;
			int i = 0;
			while(i < lines.Count)
			{
				if(lines[i].Type == DiffLineTypeEnum.Context) { i++; continue; }

				List<DiffLineModel> removed = [];
				while(i < lines.Count && lines[i].Type == DiffLineTypeEnum.Removed)
				{
					removed.Add(lines[i++]);
				}

				List<DiffLineModel> added = [];
				while(i < lines.Count && lines[i].Type == DiffLineTypeEnum.Added)
				{
					added.Add(lines[i++]);
				}

				int pairCount = Math.Min(removed.Count, added.Count);
				for(int j = 0; j < pairCount; j++)
				{
					(List<(int Start, int Length)>? leftSpans, List<(int Start, int Length)>? rightSpans) = InlineDiffComputer.Compute(removed[j].Content, added[j].Content);
					if(leftSpans.Count > 0)
					{
						result[removed[j]] = leftSpans;
					}

					if(rightSpans.Count > 0)
					{
						result[added[j]] = rightSpans;
					}
				}
			}
		}

		return result;
	}

	static string SerializeSpans(List<(int Start, int Length)> spans) =>
		"[" + string.Join(",", spans.Select(s => $"[{s.Start},{s.Length}]")) + "]";

	const int expandStep = 10;

	void ExpandAbove(DiffHunkModel hunk)
	{
		_hunkExpansion.TryGetValue(hunk, out (int Above, int Below) cur);
		_hunkExpansion[hunk] = (cur.Above + expandStep, cur.Below);
		_needsHighlight = true;
	}

	void ExpandBelow(DiffHunkModel hunk)
	{
		_hunkExpansion.TryGetValue(hunk, out (int Above, int Below) cur);
		_hunkExpansion[hunk] = (cur.Above, cur.Below + expandStep);
		_needsHighlight = true;
	}

	public async Task ExpandAllAsync()
	{
		if(_parsedDiff is null || _fileLines is null)
		{
			return;
		}

		int maxLines = _fileLines.Length;
		foreach(DiffHunkModel hunk in _parsedDiff.Hunks)
		{
			_hunkExpansion[hunk] = (maxLines, maxLines);
		}

		_needsHighlight = true;
		await InvokeAsync(StateHasChanged);
	}

	bool CanExpandAbove(DiffHunkModel hunk)
	{
		if(_fileLines is null)
		{
			return false;
		}

		_hunkExpansion.TryGetValue(hunk, out (int Above, int Below) cur);
		return hunk.NewStartLine - cur.Above > FloorAbove(hunk);
	}

	bool CanExpandBelow(DiffHunkModel hunk)
	{
		if(_fileLines is null)
		{
			return false;
		}

		_hunkExpansion.TryGetValue(hunk, out (int Above, int Below) cur);
		int lastNew = LastNewLine(hunk);
		return lastNew + cur.Below < CeilingBelow(hunk) - 1;
	}

	static int LastNewLine(DiffHunkModel hunk)
	{
		for(int i = hunk.Lines.Count - 1; i >= 0; i--)
		{
			if(hunk.Lines[i].NewLineNumber.HasValue)
			{
				return hunk.Lines[i].NewLineNumber!.Value;
			}
		}

		return hunk.NewStartLine;
	}

	static int LastOldLine(DiffHunkModel hunk)
	{
		for(int i = hunk.Lines.Count - 1; i >= 0; i--)
		{
			if(hunk.Lines[i].OldLineNumber.HasValue)
			{
				return hunk.Lines[i].OldLineNumber!.Value;
			}
		}

		return hunk.OldStartLine;
	}

	int? NextHunkNewStart(DiffHunkModel hunk)
	{
		if(_parsedDiff is null)
		{
			return null;
		}

		int idx = _parsedDiff.Hunks.IndexOf(hunk);
		return idx >= 0 && idx < _parsedDiff.Hunks.Count - 1 ? _parsedDiff.Hunks[idx + 1].NewStartLine : null;
	}

	int? NextHunkOldStart(DiffHunkModel hunk)
	{
		if(_parsedDiff is null)
		{
			return null;
		}

		int idx = _parsedDiff.Hunks.IndexOf(hunk);
		return idx >= 0 && idx < _parsedDiff.Hunks.Count - 1 ? _parsedDiff.Hunks[idx + 1].OldStartLine : null;
	}

	// The lowest new-file line that expand-above may show for this hunk.
	// Prevents overlap with the previous hunk's own content + its expand-below lines.
	int FloorAbove(DiffHunkModel hunk)
	{
		if(_parsedDiff is null)
		{
			return 1;
		}

		int idx = _parsedDiff.Hunks.IndexOf(hunk);
		if(idx <= 0)
		{
			return 1;
		}

		DiffHunkModel prev = _parsedDiff.Hunks[idx - 1];
		_hunkExpansion.TryGetValue(prev, out (int Above, int Below) prevExp);
		return LastNewLine(prev) + prevExp.Below + 1;
	}

	// One past the highest new-file line that expand-below may show for this hunk.
	// Expand-below takes priority: ceiling is always the next hunk's start, regardless
	// of how far the next hunk has expanded above. FloorAbove on the next hunk accounts
	// for this side's expansion, so the two never overlap.
	int CeilingBelow(DiffHunkModel hunk)
	{
		if(_parsedDiff is null)
		{
			return _fileLines?.Length + 1 ?? int.MaxValue;
		}

		int idx = _parsedDiff.Hunks.IndexOf(hunk);
		if(idx < 0 || idx >= _parsedDiff.Hunks.Count - 1)
		{
			return (_fileLines?.Length ?? 0) + 1;
		}

		DiffHunkModel next = _parsedDiff.Hunks[idx + 1];
		return next.NewStartLine;
	}

	List<(int? OldLine, int NewLine, string Content)> GetExpandedAbove(DiffHunkModel hunk)
	{
		if(_fileLines is null)
		{
			return [];
		}

		_hunkExpansion.TryGetValue(hunk, out (int Above, int Below) cur);
		if(cur.Above <= 0)
		{
			return [];
		}

		int floor = FloorAbove(hunk);
		int newStart = Math.Max(floor, hunk.NewStartLine - cur.Above);
		int count = hunk.NewStartLine - newStart;
		// count can be zero or negative when a neighbouring hunk's expand-below has
		// already consumed the entire inter-hunk gap — return empty rather than throwing.
		if(count <= 0)
		{
			return [];
		}

		int oldOffset = hunk.OldStartLine - hunk.NewStartLine;
		bool hasOldSide = hunk.OldStartLine > 0;
		List<(int?, int, string)> result = new(count);
		for(int i = 0; i < count; i++)
		{
			int newLine = newStart + i;
			int idx = newLine - 1;
			if(idx >= 0 && idx < _fileLines.Length)
			{
				int? oldLine = hasOldSide ? newLine + oldOffset : null;
				result.Add((oldLine, newLine, _fileLines[idx]));
			}
		}
		return result;
	}

	List<(int? OldLine, int NewLine, string Content)> GetExpandedBelow(DiffHunkModel hunk)
	{
		if(_fileLines is null)
		{
			return [];
		}

		_hunkExpansion.TryGetValue(hunk, out (int Above, int Below) cur);
		if(cur.Below <= 0)
		{
			return [];
		}

		int lastNew = LastNewLine(hunk);
		int lastOld = LastOldLine(hunk);
		bool hasOldSide = lastOld > 0;
		int ceiling = CeilingBelow(hunk);
		List<(int?, int, string)> result = [];
		for(int i = 0; i < cur.Below; i++)
		{
			int newLine = lastNew + 1 + i;
			if(newLine >= ceiling)
			{
				break;
			}

			int idx = newLine - 1;
			if(idx < 0 || idx >= _fileLines.Length)
			{
				break;
			}

			int? oldLine = hasOldSide ? lastOld + 1 + i : null;
			result.Add((oldLine, newLine, _fileLines[idx]));
		}
		return result;
	}

	string? _pendingFilePath;

	protected override void OnParametersSet()
	{
		if(Diff != _prevDiff || FilePath != _prevFilePath || SplitView != _prevSplitView)
		{
			_parsedDiff = null;
			_splitRows = [];
			_inlineSpans = [];
			_fileLines = null;
			_mediaDataUrl = null;
			_mediaLoading = false;
			_mediaTooBig = false;
			_hunkExpansion.Clear();

			if(_fileCategory == FileCategory.Binary)
			{
				_parsedDiff = null;
				_splitRows = [];
				_inlineSpans = [];
				_fileLines = null;
			}
			else if(_fileCategory == FileCategory.Image || _fileCategory == FileCategory.Video)
			{
				_parsedDiff = null;
				_splitRows = [];
				_inlineSpans = [];
				_fileLines = null;
				_mediaLoading = true;
				_ = LoadMediaAsync(FilePath);
			}
			else
			{
				_parsedDiff = DiffParser.Parse(Diff);
				_splitRows = _parsedDiff?.Hunks.ToDictionary(h => h, DiffParser.BuildSplitRows) ?? [];
				_inlineSpans = SplitView ? [] : ComputeInlineSpans(_parsedDiff);
				_needsHighlight = true;
				_fileLines = null;
				_ = LoadFileLinesAsync(FilePath);
			}
		}
	}

	const long MaxImageBytes = 20 * 1024 * 1024;
	const long MaxVideoBytes = 50 * 1024 * 1024;

	async Task LoadMediaAsync(string? filePath)
	{
		string? dataUrl = null;
		bool tooBig = false;

		if(filePath is not null && File.Exists(filePath))
		{
			try
			{
				long maxBytes = _fileCategory == FileCategory.Image ? MaxImageBytes : MaxVideoBytes;
				FileInfo info = new(filePath);
				if(info.Length <= maxBytes)
				{
					byte[] bytes = await Task.Run(() => File.ReadAllBytes(filePath));
					string ext = Path.GetExtension(filePath).ToLowerInvariant();
					string mimeType = _fileCategory == FileCategory.Image
						? GetImageMimeType(ext)
						: GetVideoMimeType(ext);
					dataUrl = $"data:{mimeType};base64,{Convert.ToBase64String(bytes)}";
				}
				else
				{
					tooBig = true;
				}
			}
			catch { }
		}

		if(filePath == _pendingFilePath)
		{
			_mediaDataUrl = dataUrl;
			_mediaTooBig = tooBig;
			_mediaLoading = false;
			await InvokeAsync(StateHasChanged);
		}
	}

	static string GetImageMimeType(string ext) => ext switch
	{
		".jpg" or ".jpeg" => "image/jpeg",
		".gif" => "image/gif",
		".bmp" => "image/bmp",
		".ico" => "image/x-icon",
		".svg" => "image/svg+xml",
		".webp" => "image/webp",
		".tiff" or ".tif" => "image/tiff",
		_ => "image/png"
	};

	static string GetVideoMimeType(string ext) => ext switch
	{
		".avi" => "video/x-msvideo",
		".mov" => "video/quicktime",
		".wmv" => "video/x-ms-wmv",
		".flv" => "video/x-flv",
		".webm" => "video/webm",
		".mkv" => "video/x-matroska",
		".m4v" => "video/x-m4v",
		".ogv" or ".ogg" => "video/ogg",
		_ => "video/mp4"
	};

	async Task LoadFileLinesAsync(string? filePath)
	{
		string[]? lines = null;
		if(filePath is not null)
		{
			try
			{
				lines = await Task.Run(() => File.Exists(filePath) ? File.ReadAllLines(filePath) : null);
			}
			catch { }
		}

		if(filePath == _pendingFilePath)
		{
			_fileLines = lines;
			_needsHighlight = true;
			await InvokeAsync(StateHasChanged);
		}
	}

	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		if(!_needsHighlight)
		{
			return;
		}

		_needsHighlight = false;

		string language = DetectLanguage();
		try
		{
			if(SplitView)
			{
				await _jsRuntime.InvokeVoidAsync("cockpit.setupSplitDiffScroll", _diffSplitLeftId, _diffSplitRightId);
				await _jsRuntime.InvokeVoidAsync("cockpit.highlightDiffCells", _diffSplitLeftId, language);
				await _jsRuntime.InvokeVoidAsync("cockpit.highlightDiffCells", _diffSplitRightId, language);
			}
			else
			{
				await _jsRuntime.InvokeVoidAsync("cockpit.highlightDiffCells", _diffInlineId, language);
			}
		}
		catch { /* ignore if hljs unavailable */ }
	}
}