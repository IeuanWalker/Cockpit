using Cockpit.Features.Terminal;
using Cockpit.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using XtermBlazor;

namespace Cockpit.Components.Pages.ChatPanel;

public partial class TerminalPanel : IDisposable
{
	[Inject] TerminalFeature _terminalFeature { get; set; } = default!;
	[Inject] IJSRuntime _jsRuntime { get; set; } = default!;
	[Inject] UIStateService _uiState { get; set; } = default!;
	[Inject] ILogger<TerminalPanel> _logger { get; set; } = default!;

	[Parameter] public bool IsOpen { get; set; }
	[Parameter] public string SessionId { get; set; } = string.Empty;
	[Parameter] public string? WorkingDirectory { get; set; }

	const int domLayoutSettleDelayMs = 100; // Delay to allow DOM layout to settle after resize
	bool _disposed;
	Xterm? _terminal;
	string _terminalId = string.Empty;
	bool _isSessionActive = false;
	bool _hasRestoredBuffer = false;
	bool _isTerminalInitialized = false;
	bool _isFullscreen = false;
	CancellationTokenSource? _resizeCts;
	string _containerStyle = "height: 30%; display: flex; flex-direction: column; z-index: 50;";
	string _containerClasses = string.Empty;
	readonly HashSet<string> _addons = ["addon-fit"];
	readonly TerminalOptions _options = new()
	{
		CursorBlink = true,
		CursorStyle = CursorStyle.Block,
		FontSize = 13,
		FontFamily = "Consolas, 'Courier New', monospace"
	};

	bool _showAddToMessagePopup;

	void OpenAddToMessagePopup() => _showAddToMessagePopup = true;
	void CloseAddToMessagePopup() => _showAddToMessagePopup = false;

	protected override void OnInitialized()
	{
		_terminalId = $"terminal-{SessionId}";
		_terminalFeature.OnDataReceived += OnTerminalData;
		_uiState.OnStateChanged += OnUIStateChanged;
	}

	protected override void OnParametersSet()
	{
		string nextTerminalId = $"terminal-{SessionId}";
		if(_terminalId != nextTerminalId)
		{
			_terminalId = nextTerminalId;
			_isTerminalInitialized = false;
			_hasRestoredBuffer = false;
		}

		if(!IsOpen)
		{
			_isSessionActive = false;
			_hasRestoredBuffer = false;
			_isTerminalInitialized = false;
		}
	}

	void OnUIStateChanged()
	{
		_ = InvokeAsync(async () => await Resize());
	}

	async Task ToggleFullscreen()
	{
		_isFullscreen = !_isFullscreen;
		_containerStyle = _isFullscreen
			? "position: fixed; inset: 0; z-index: 50; height: 100vh; width: 100vw; display: flex; flex-direction: column;"
			: "height: 30%; display: flex; flex-direction: column; z-index: 50;";
		_containerClasses = _isFullscreen ? "bg-vscode-sidebar" : string.Empty;

		// Trigger re-render
		await InvokeAsync(StateHasChanged);

		// Cancel any resize triggered by the DOM change (ResizeObserver fires in ~50ms)
		// so the explicit resize below is the authoritative one.
		_resizeCts?.Cancel();

		// Wait for the DOM layout to fully settle at the new size
		await Task.Delay(150);

		// Drive a single clean resize + full viewport refresh
		await Resize();

		// Focus the terminal
		if(_terminal is not null)
		{
			await _terminal.Focus();
		}
	}

	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		if(!IsOpen || _terminal is null || _isTerminalInitialized || string.IsNullOrWhiteSpace(SessionId))
		{
			return;
		}

		await Task.Delay(100);

		// Try to create session (returns true if new, false if already exists)
		string workDir = !string.IsNullOrEmpty(WorkingDirectory) ? WorkingDirectory : Directory.GetCurrentDirectory();
		await _terminalFeature.CreateSession(SessionId, workDir);

		// Restore buffered output (works for both new and existing sessions)
		if(!_hasRestoredBuffer)
		{
			string bufferedOutput = _terminalFeature.GetBufferedOutput(SessionId);
			if(!string.IsNullOrEmpty(bufferedOutput))
			{
				try
				{
					await _terminal.Write(bufferedOutput);
					_hasRestoredBuffer = true;
				}
				catch(Exception ex)
				{
					_logger.LogDebug(ex, "Failed to restore buffered output for session {SessionId}", SessionId);
				}
			}
		}

		_isSessionActive = true;
		_isTerminalInitialized = true;

		await _terminal.Focus();
		await Resize();
	}

	DotNetObjectReference<TerminalPanel>? _dotNetRef;

	async Task OnTerminalFirstRender()
	{
		_dotNetRef = DotNetObjectReference.Create(this);
		await _jsRuntime.InvokeVoidAsync("xtermInterop.registerWindowResize", _terminalId, _dotNetRef);
		await _jsRuntime.InvokeVoidAsync("xtermInterop.observeElementResize", _terminalId, _dotNetRef);
		await Resize();
	}

	[JSInvokable]
	public async Task OnTerminalWindowResize()
	{
		await Resize();
	}

	public async Task Resize()
	{
		if(_terminal is null)
		{
			return;
		}

		// Debounce: cancel any in-flight resize and schedule a fresh one
		_resizeCts?.Cancel();
		_resizeCts?.Dispose();
		_resizeCts = new CancellationTokenSource();
		CancellationToken token = _resizeCts.Token;

		try
		{
			// Wait briefly for the DOM/container layout to settle after a resize
			await Task.Delay(domLayoutSettleDelayMs, token);

			// IMPORTANT: Call fit() once to resize the terminal to match the container
			await _terminal.Addon("addon-fit").InvokeVoidAsync("fit");

			// Clear texture atlas to fix any stale rendering artifacts
			await _jsRuntime.InvokeVoidAsync("xtermInterop.triggerResize", _terminalId);

			// Query the actual cols/rows after fit
			TerminalSize? size = await _jsRuntime.InvokeAsync<TerminalSize?>("xtermInterop.getTerminalSize", _terminalId);
			if(size is not null && size.Cols > 0 && size.Rows > 0)
			{
				// Resize the PTY to match the terminal's new dimensions
				_terminalFeature.ResizePty(SessionId, size.Cols, size.Rows);
			}
		}
		catch(OperationCanceledException)
		{
			// Expected: a newer resize superseded this one
		}
		catch(Exception ex)
		{
			_logger.LogDebug(ex, "Failed to resize terminal for session {SessionId}", SessionId);
		}
	}

	class TerminalSize
	{
		public int Cols { get; set; }
		public int Rows { get; set; }
	}

	async Task OnTerminalInput(string data)
	{
		await _terminalFeature.WriteAsync(SessionId, data);
	}

	// Event handler must be async void to match event signature
	// Top-level exception handling ensures app doesn't crash
	async void OnTerminalData(string sessionId, string data)
	{
		if(sessionId != SessionId || _terminal is null || !_isSessionActive)
		{
			return;
		}

		try
		{
			await InvokeAsync(async () =>
			{
				try
				{
					await _terminal.Write(data);
				}
				catch(Exception ex)
				{
					_logger.LogDebug(ex, "Failed to write data to terminal for session {SessionId}", sessionId);
				}
			});
		}
		catch(Exception ex)
		{
			// Log unhandled exceptions from async void to prevent app crash
			_logger.LogError(ex, "Unhandled exception in terminal data event handler for session {SessionId}", sessionId);

			// Surface a minimal error indication to the user in the terminal itself
			try
			{
				if(_terminal is not null)
				{
					await InvokeAsync(async () =>
					{
						try
						{
							await _terminal.Write("\r\n[Error] Failed to deliver terminal output. See logs for details.\r\n");
						}
						catch(Exception writeEx)
						{
							// Swallow any secondary errors while reporting the failure
							_logger.LogDebug(writeEx, "Failed to write error message to terminal for session {SessionId}", sessionId);
						}
					});
				}
			}
			catch(Exception notifyEx)
			{
				// As a final safeguard, ignore any errors while attempting to notify the user
				_logger.LogDebug(notifyEx, "Failed to notify user of terminal error for session {SessionId}", sessionId);
			}
		}
	}

	protected virtual void Dispose(bool disposing)
	{
		if(!_disposed)
		{
			if(disposing)
			{
				_isSessionActive = false;
				_resizeCts?.Cancel();
				_resizeCts?.Dispose();
				_terminalFeature.OnDataReceived -= OnTerminalData;
				_uiState.OnStateChanged -= OnUIStateChanged;
				_dotNetRef?.Dispose();
				//! Important: Don't close the session - keep it alive for when we switch back
			}
			// Free unmanaged resources if any
			_disposed = true;
		}
	}

	async Task Reset()
	{
		if(_terminal is null)
		{
			return;
		}

		// Clear buffered output and restart PTY
		_terminalFeature.SoftClear(SessionId);
		await _terminalFeature.RestartSession(SessionId, WorkingDirectory ?? Directory.GetCurrentDirectory());
		_hasRestoredBuffer = false;
		_isSessionActive = true;

		// Reset frontend terminal view and focus
		await _terminal.Reset();
		await _terminal.Focus();

		// Small delay then resize to current container
		await Task.Delay(50);
		await Resize();
	}

	public void Dispose()
	{
		Dispose(true);
		GC.SuppressFinalize(this);
	}
}
