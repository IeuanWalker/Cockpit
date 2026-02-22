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
	readonly UIStateFeature _uiStateFeature;
	readonly ILogger<TerminalAddToMessagePopup> _logger;

	public TerminalAddToMessagePopup(
		IJSRuntime jsRuntime,
		UIStateFeature uiStateFeature,
		ILogger<TerminalAddToMessagePopup> logger)
	{
		_jsRuntime = jsRuntime;
		_uiStateFeature = uiStateFeature;
		_logger = logger;
	}

	public int SelectedLineCount { get; set; } = 50;
	public int TotalLineCount { get; set; }
	public string PreviewText { get; set; } = string.Empty;

	PopupBase _popup = default!;

	public async Task Open()
	{
		PreviewText = string.Empty;
		SelectedLineCount = 50;

		_popup.Open();

		await OnParametersSetAsync();
	}

	protected override async Task OnParametersSetAsync()
	{
		TotalLineCount = await GetRenderedLineCount();
		SelectedLineCount = Math.Min(100, TotalLineCount > 0 ? TotalLineCount : 100);
		await UpdatePreview();
	}

	async Task UpdatePreview()
	{
		if(SelectedLineCount > 0)
		{
			try
			{
				PreviewText = await _jsRuntime.InvokeAsync<string>("xtermInterop.getTerminalText", TerminalId, SelectedLineCount);
			}
			catch(Exception ex)
			{
				_logger.LogDebug(ex, "Failed to get terminal text for session {SessionId}", SessionId);
				PreviewText = string.Empty;
			}
		}
		else
		{
			PreviewText = string.Empty;
		}
		await InvokeAsync(StateHasChanged);
	}

	async Task<int> GetRenderedLineCount()
	{
		try
		{
			string text = await _jsRuntime.InvokeAsync<string>("xtermInterop.getTerminalText", TerminalId, int.MaxValue);
			return string.IsNullOrEmpty(text) ? 0 : text.Split('\n').Length;
		}
		catch
		{
			return 0;
		}
	}

	void HandleClose()
	{
		_popup.Close();
	}

	void HandleConfirm()
	{
		if(!string.IsNullOrEmpty(PreviewText))
		{
			_uiStateFeature.AppendChatInput($"Terminal output:\n```\n{PreviewText}\n```");
		}

		_popup.Close();
	}
}
