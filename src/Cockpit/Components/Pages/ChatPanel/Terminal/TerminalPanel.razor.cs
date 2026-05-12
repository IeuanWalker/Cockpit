using Cockpit.Features.Terminal;
using Cockpit.Features.UIState;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using XtermBlazor;

namespace Cockpit.Components.Pages.ChatPanel.Terminal;

public partial class TerminalPanel : IAsyncDisposable, IDisposable
{
	[Parameter] public bool IsOpen { get; set; }
	[Parameter] public string SessionId { get; set; } = string.Empty;
	[Parameter] public string? WorkingDirectory { get; set; }

	readonly TerminalFeature _terminalFeature;
	readonly IJSRuntime _jsRuntime;
	readonly IUIStateFeature _uiStateFeature;
	readonly ILogger<TerminalPanel> _logger;

	public TerminalPanel(
		TerminalFeature terminalFeature,
		IJSRuntime jsRuntime,
		IUIStateFeature uiStateFeature,
		ILogger<TerminalPanel> logger)
	{
		_terminalFeature = terminalFeature;
		_jsRuntime = jsRuntime;
		_uiStateFeature = uiStateFeature;
		_logger = logger;
	}



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

	TerminalAddToMessagePopup _terminalAddToMessagePopup = default!;

	async void OpenAddToMessagePopup()
	{
		try
		{
			await _terminalAddToMessagePopup.Open();
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Failed to open terminal add-to-message popup");
		}
	}

	protected override void OnInitialized()
	{
		_terminalId = $"terminal-{SessionId}";
		_terminalFeature.OnDataReceived += OnTerminalData;
		_uiStateFeature.OnStateChanged += OnUIStateChanged;
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
		_ = InvokeAsync(async () =>
		{
			try
			{
				await Resize();
			}
			catch(Exception ex)
			{
				_logger.LogDebug(ex, "Failed to resize terminal after UI state change for session {SessionId}", SessionId);
			}
		});
	}

	async Task ToggleFullscreen()
	{
		_isFullscreen = !_isFullscreen;
		_containerStyle = _isFullscreen
			? "position: fixed; inset: 0; z-index: 50; height: 100vh; width: 100vw; display: flex; flex-direction: column;"
			: "height: 30%; display: flex; flex-direction: column; z-index: 50;";
		_containerClasses = _isFullscreen ? "bg-sidebar" : string.Empty;

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

		await Task.Delay(100); // Allow the DOM to settle before accessing the terminal element

		// Re-check after delay: panel may have been closed or disposed during the 100ms wait
		if(!IsOpen || _terminal is null || _isTerminalInitialized || _disposed)
		{
			return;
		}

		// Fit the terminal to its container BEFORE creating the PTY so the shell
		// starts with dimensions that match the visible terminal area.  This
		// prevents the initial output being formatted for the wrong column width
		// which causes text overlap / misalignment.
		int cols = 120;
		int rows = 30;
		try
		{
			await _terminal.Addon("addon-fit").InvokeVoidAsync("fit");
			TerminalSize? size = await _jsRuntime.InvokeAsync<TerminalSize?>("xtermInterop.getTerminalSize", _terminalId);
			if(size is not null && size.Cols > 0 && size.Rows > 0)
			{
				cols = size.Cols;
				rows = size.Rows;
			}
		}
		catch(Exception ex)
		{
			_logger.LogDebug(ex, "Failed to pre-fit terminal for session {SessionId}, using default dimensions", SessionId);
		}

		// Try to create session (true = new, false = already exists or failed)
		string workDir = !string.IsNullOrEmpty(WorkingDirectory) ? WorkingDirectory : Directory.GetCurrentDirectory();
		await _terminalFeature.CreateSession(SessionId, workDir, cols, rows);

		// If no session exists (creation genuinely failed), bail out so the UI
		// is not left showing an active terminal with no backing process.
		if(_terminalFeature.GetSession(SessionId) is null)
		{
			_logger.LogError("Failed to create terminal session for {SessionId}", SessionId);
			return;
		}

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

		_isTerminalInitialized = true;

		await _terminal.Focus();

		// Final resize to sync everything, then enable data flow from the PTY.
		// Setting _isSessionActive after resize ensures no output arrives while
		// the terminal is still at mismatched dimensions.
		await Resize();
		_isSessionActive = true;
	}

	DotNetObjectReference<TerminalPanel>? _dotNetRef;
	IJSObjectReference? _windowResizeRef;
	IJSObjectReference? _elementResizeRef;

	async Task OnTerminalFirstRender()
	{
		_dotNetRef = DotNetObjectReference.Create(this);
		_windowResizeRef = await _jsRuntime.InvokeAsync<IJSObjectReference>("xtermInterop.registerWindowResize", _terminalId, _dotNetRef);
		_elementResizeRef = await _jsRuntime.InvokeAsync<IJSObjectReference>("xtermInterop.observeElementResize", _terminalId, _dotNetRef);
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

			// Get the dimensions the terminal WOULD resize to, without applying yet.
			// This lets us resize the PTY first so the shell is already at the
			// correct dimensions when xterm.js reflows content.
			TerminalSize? proposed = await _terminal.Addon("addon-fit").InvokeAsync<TerminalSize?>("proposeDimensions");
			if(proposed is not null && proposed.Cols > 0 && proposed.Rows > 0)
			{
				// Resize PTY BEFORE fit() so the shell processes the new dimensions
				// before xterm.js reflows content — prevents cursor misplacement.
				_terminalFeature.ResizePty(SessionId, proposed.Cols, proposed.Rows);
			}

			// Now apply the resize to xterm.js — the PTY is already at the new size
			await _terminal.Addon("addon-fit").InvokeVoidAsync("fit");

			// Clear texture atlas to fix any stale rendering artifacts
			await _jsRuntime.InvokeVoidAsync("xtermInterop.triggerResize", _terminalId);

			// If proposeDimensions wasn't available, fall back to querying after fit
			if(proposed is null)
			{
				TerminalSize? size = await _jsRuntime.InvokeAsync<TerminalSize?>("xtermInterop.getTerminalSize", _terminalId);
				if(size is not null && size.Cols > 0 && size.Rows > 0)
				{
					_terminalFeature.ResizePty(SessionId, size.Cols, size.Rows);
				}
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

	async void OnTerminalData(string sessionId, string data)
	{
		if(sessionId != SessionId || _terminal is null || !_isSessionActive)
		{
			return;
		}

		try
		{
			await InvokeAsync(async () => await _terminal.Write(data));
		}
		catch(Exception ex)
		{
			_logger.LogDebug(ex, "Failed to deliver terminal output for session {SessionId}", sessionId);
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
				_uiStateFeature.OnStateChanged -= OnUIStateChanged;
				_dotNetRef?.Dispose();
				// Important: Don't close the session - keep it alive for when we switch back
			}
			// Free unmanaged resources if any
			_disposed = true;
		}
	}

	public async ValueTask DisposeAsync()
	{
		// Disconnect JS observers before disposing the DotNetObjectReference so
		// the observers stop invoking .NET callbacks and don't hold a stale ref.
		try
		{
			if(_windowResizeRef is not null)
			{
				await _windowResizeRef.InvokeVoidAsync("dispose");
				await _windowResizeRef.DisposeAsync();
				_windowResizeRef = null;
			}

			if(_elementResizeRef is not null)
			{
				await _elementResizeRef.InvokeVoidAsync("dispose");
				await _elementResizeRef.DisposeAsync();
				_elementResizeRef = null;
			}
		}
		catch(Exception ex)
		{
			_logger.LogDebug(ex, "Failed to unregister terminal observers for session {SessionId}", SessionId);
		}

		Dispose(true);
		GC.SuppressFinalize(this);
	}

	async Task Reset()
	{
		if(_terminal is null)
		{
			return;
		}

		_isSessionActive = false;

		// Get current terminal dimensions before restarting
		int cols = 120;
		int rows = 30;
		try
		{
			TerminalSize? size = await _jsRuntime.InvokeAsync<TerminalSize?>("xtermInterop.getTerminalSize", _terminalId);
			if(size is not null && size.Cols > 0 && size.Rows > 0)
			{
				cols = size.Cols;
				rows = size.Rows;
			}
		}
		catch(Exception ex)
		{
			_logger.LogDebug(ex, "Failed to get terminal size for reset, using defaults");
		}

		// Restart PTY (RestartSession internally clears the buffer before restarting)
		await _terminalFeature.RestartSession(SessionId, WorkingDirectory ?? Directory.GetCurrentDirectory(), cols, rows);
		_hasRestoredBuffer = false;

		// Reset frontend terminal view and focus
		await _terminal.Reset();
		await _terminal.Focus();

		// Small delay then resize to current container, then enable data flow
		await Task.Delay(50);
		await Resize();
		_isSessionActive = true;
	}

	public void Dispose()
	{
		Dispose(true);
		GC.SuppressFinalize(this);
	}
}
