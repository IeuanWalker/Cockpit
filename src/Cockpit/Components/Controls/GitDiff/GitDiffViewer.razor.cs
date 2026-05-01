using Cockpit.Components.Controls.GitDiff.Models;
using Cockpit.Features.Git.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Cockpit.Components.Controls.GitDiff;

public partial class GitDiffViewer : ComponentBase
{
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
	bool _needsHighlight;

	string? _prevDiff;
	string? _prevFilePath;
	bool _prevSplitView;

	readonly string _diffInlineId = $"diff-inline-{Guid.NewGuid():N}";
	readonly string _diffLeftId = $"diff-left-{Guid.NewGuid():N}";
	readonly string _diffRightId = $"diff-right-{Guid.NewGuid():N}";

	string _fileName => string.IsNullOrEmpty(FilePath) ? string.Empty : Path.GetFileName(FilePath);

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
		var result = new Dictionary<DiffLineModel, List<(int Start, int Length)>>();
		if(diff is null)
			return result;

		foreach(DiffHunkModel hunk in diff.Hunks)
		{
			List<DiffLineModel> lines = hunk.Lines;
			int i = 0;
			while(i < lines.Count)
			{
				if(lines[i].Type == DiffLineTypeEnum.Context) { i++; continue; }

				var removed = new List<DiffLineModel>();
				while(i < lines.Count && lines[i].Type == DiffLineTypeEnum.Removed)
					removed.Add(lines[i++]);

				var added = new List<DiffLineModel>();
				while(i < lines.Count && lines[i].Type == DiffLineTypeEnum.Added)
					added.Add(lines[i++]);

				int pairCount = Math.Min(removed.Count, added.Count);
				for(int j = 0; j < pairCount; j++)
				{
					var (leftSpans, rightSpans) = InlineDiffComputer.Compute(removed[j].Content, added[j].Content);
					if(leftSpans.Count > 0) result[removed[j]] = leftSpans;
					if(rightSpans.Count > 0) result[added[j]] = rightSpans;
				}
			}
		}

		return result;
	}

	static string SerializeSpans(List<(int Start, int Length)> spans) =>
		"[" + string.Join(",", spans.Select(s => $"[{s.Start},{s.Length}]")) + "]";

	protected override void OnParametersSet()
	{
		if(Diff != _prevDiff || FilePath != _prevFilePath || SplitView != _prevSplitView)
		{
			_parsedDiff = DiffParser.Parse(Diff);
			_splitRows = _parsedDiff?.Hunks.ToDictionary(h => h, DiffParser.BuildSplitRows) ?? [];
			_inlineSpans = ComputeInlineSpans(_parsedDiff);
			_needsHighlight = true;

			_prevDiff = Diff;
			_prevFilePath = FilePath;
			_prevSplitView = SplitView;
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
				await _jsRuntime.InvokeVoidAsync("cockpit.highlightDiffCells", _diffLeftId, language);
				await _jsRuntime.InvokeVoidAsync("cockpit.highlightDiffCells", _diffRightId, language);
				await _jsRuntime.InvokeVoidAsync("cockpit.setupSplitDiffScroll", _diffLeftId, _diffRightId);
			}
			else
			{
				await _jsRuntime.InvokeVoidAsync("cockpit.highlightDiffCells", _diffInlineId, language);
			}
		}
		catch { /* ignore if hljs unavailable */ }
	}
}
