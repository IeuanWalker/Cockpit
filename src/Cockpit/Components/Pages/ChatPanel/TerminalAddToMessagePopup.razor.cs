using Cockpit.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Cockpit.Components.Pages.ChatPanel;

public partial class TerminalAddToMessagePopup : ComponentBase
{
	[Inject] IJSRuntime _js { get; set; } = default!;
	[Inject] UIStateService _uiState { get; set; } = default!;
	[Inject] ILogger<TerminalAddToMessagePopup> _logger { get; set; } = default!;

	[Parameter] public bool IsOpen { get; set; }
	[Parameter] public string TerminalId { get; set; } = string.Empty;
	[Parameter] public string SessionId { get; set; } = string.Empty;
	[Parameter] public EventCallback OnClose { get; set; }

	bool _wasOpen;
	int _selectedLineCount = 50;
	int _totalLineCount;
	string _previewText = string.Empty;

	protected override async Task OnParametersSetAsync()
	{
		if(IsOpen && !_wasOpen)
		{
			_totalLineCount = await GetRenderedLineCount();
			_selectedLineCount = Math.Min(100, _totalLineCount > 0 ? _totalLineCount : 100);
			await UpdatePreview();
		}
		else if(!IsOpen)
		{
			_previewText = string.Empty;
		}
		_wasOpen = IsOpen;
	}

	async Task UpdatePreview()
	{
		if(_selectedLineCount > 0)
		{
			try
			{
				_previewText = await _js.InvokeAsync<string>("xtermInterop.getTerminalText", TerminalId, _selectedLineCount);
			}
			catch(Exception ex)
			{
				_logger.LogDebug(ex, "Failed to get terminal text for session {SessionId}", SessionId);
				_previewText = string.Empty;
			}
		}
		else
		{
			_previewText = string.Empty;
		}
	}

	async Task<int> GetRenderedLineCount()
	{
		try
		{
			string text = await _js.InvokeAsync<string>("xtermInterop.getTerminalText", TerminalId, int.MaxValue);
			return string.IsNullOrEmpty(text) ? 0 : text.Split('\n').Length;
		}
		catch
		{
			return 0;
		}
	}

	async Task HandleClose()
	{
		await OnClose.InvokeAsync();
	}

	async Task HandleConfirm()
	{
		if(!string.IsNullOrEmpty(_previewText))
		{
			_uiState.AppendChatInput($"Terminal output:\n```\n{_previewText}\n```");
		}
		await OnClose.InvokeAsync();
	}
}
