using Cockpit.Components.Controls;
using Cockpit.Features.UIState;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Cockpit.Components.Pages.ChatPanel.Terminal;

public partial class TerminalAddToMessagePopup : ComponentBase
{
	[Parameter] public string TerminalId { get; set; } = string.Empty;
	[Parameter] public string SessionId { get; set; } = string.Empty;

	readonly IJSRuntime _jsRuntime;
	readonly IUIStateFeature _uiStateFeature;
	readonly ILogger<TerminalAddToMessagePopup> _logger;

	public TerminalAddToMessagePopup(
		IJSRuntime jsRuntime,
		IUIStateFeature uiStateFeature,
		ILogger<TerminalAddToMessagePopup> logger)
	{
		_jsRuntime = jsRuntime;
		_uiStateFeature = uiStateFeature;
		_logger = logger;
	}

	public int SelectedLineCount { get; set; } = 50;
	public int TotalLineCount { get; set; }
	public string PreviewText { get; set; } = string.Empty;
	int _previewLineCount;
	string[] _allLines = [];

	PopupBase _popup = default!;

	public async Task Open()
	{
		SelectedLineCount = 50;
		PreviewText = string.Empty;
		_previewLineCount = 0;
		_allLines = [];

		_popup.Open();

		await RefreshTerminalContent();
		await InvokeAsync(StateHasChanged);
	}

	async Task RefreshTerminalContent()
	{
		try
		{
			// Cap at 10 000 lines to avoid serialising a huge payload through JS interop,
			// which can freeze the UI for sessions with very large output histories.
			string allText = await _jsRuntime.InvokeAsync<string>("xtermInterop.getTerminalText", TerminalId, 10_000);
			_allLines = string.IsNullOrEmpty(allText) ? [] : allText.Split('\n');
			TotalLineCount = _allLines.Length;
			SelectedLineCount = Math.Min(SelectedLineCount > 0 ? SelectedLineCount : 100, TotalLineCount > 0 ? TotalLineCount : 100);
		}
		catch(Exception ex)
		{
			_logger.LogDebug(ex, "Failed to fetch terminal text for session {SessionId}", SessionId);
			_allLines = [];
			TotalLineCount = 0;
		}

		BuildPreview();
	}

	void BuildPreview()
	{
		if(_allLines.Length == 0 || SelectedLineCount <= 0)
		{
			PreviewText = string.Empty;
			_previewLineCount = 0;
			return;
		}

		string[] selectedLines = _allLines.Length > SelectedLineCount
			? _allLines[^SelectedLineCount..]
			: _allLines;

		PreviewText = string.Join('\n', selectedLines);
		_previewLineCount = selectedLines.Length;
	}

	Task UpdatePreview()
	{
		BuildPreview();
		StateHasChanged();
		return Task.CompletedTask;
	}

	void HandleClose() => _popup.Close();

	void HandleConfirm()
	{
		if(!string.IsNullOrEmpty(PreviewText))
		{
			_uiStateFeature.AppendChatInput($"Terminal output:\n```\n{PreviewText}\n```");
		}

		_popup.Close();
	}
}
