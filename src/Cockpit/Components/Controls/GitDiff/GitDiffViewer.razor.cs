using Cockpit.Components.Controls.GitDiff.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Cockpit.Components.Controls.GitDiff;

public partial class GitDiffViewer : ComponentBase
{
	[Inject] IJSRuntime JS { get; set; } = default!;

	[Parameter] public string? Diff { get; set; }
	[Parameter] public string? FilePath { get; set; }
	[Parameter] public bool SplitView { get; set; }

	ParsedDiffModel? _parsedDiff;
	Dictionary<DiffHunkModel, List<SplitRowModel>> _splitRows = [];
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
		[".scss"] = "css",
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
		[".c"] = "c",
		[".h"] = "cpp",
		[".php"] = "php",
		[".rb"] = "ruby",
		[".swift"] = "swift",
		[".kt"] = "kotlin",
	};

	string DetectLanguage() =>
		string.IsNullOrEmpty(FilePath) ? "plaintext" :
		extensionLanguageMap.TryGetValue(Path.GetExtension(FilePath), out string? lang) ? lang : "plaintext";

	protected override void OnParametersSet()
	{
		if(Diff != _prevDiff || FilePath != _prevFilePath || SplitView != _prevSplitView)
		{
			_parsedDiff = DiffParser.Parse(Diff);
			_splitRows = _parsedDiff?.Hunks.ToDictionary(h => h, DiffParser.BuildSplitRows) ?? [];
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
