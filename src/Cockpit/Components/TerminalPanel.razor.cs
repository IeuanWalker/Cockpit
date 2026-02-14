using Cockpit.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using XtermBlazor;

namespace Cockpit.Components;

public partial class TerminalPanel : IDisposable
{
	bool _disposed;
	Xterm? _terminal;
	string _terminalId = string.Empty;
	bool _isSessionActive = false;
	bool _hasRestoredBuffer = false;
	bool _isFullscreen = false;
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

	[Inject] TerminalService TerminalService { get; set; } = default!;
	[Inject] IJSRuntime JS { get; set; } = default!;
	[Inject] ILogger<TerminalPanel> Logger { get; set; } = default!;
	[Parameter] public bool IsOpen { get; set; }
	[Parameter] public string SessionId { get; set; } = string.Empty;
	[Parameter] public string? WorkingDirectory { get; set; }
	[Parameter] public EventCallback OnClose { get; set; }

	[Inject] UIStateService UIState { get; set; } = default!;

	protected override void OnInitialized()
	{
		_terminalId = $"terminal-{SessionId}";
		TerminalService.OnDataReceived += OnTerminalData;
		UIState.OnStateChanged += OnUIStateChanged;
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

		// Wait for DOM to update with new size
		await Task.Delay(150);

		// Resize terminal to fit new container (this reflows content)
		await Resize();

		// Focus the terminal
		if(_terminal is not null)
		{
			await _terminal.Focus();
		}
	}

	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		if(!firstRender || !IsOpen || _terminal is null)
		{
			return;
		}

		await Task.Delay(100);

		// Try to create session (returns true if new, false if already exists)
		string workDir = !string.IsNullOrEmpty(WorkingDirectory) ? WorkingDirectory : Directory.GetCurrentDirectory();
		await TerminalService.CreateSession(SessionId, workDir);

		// Restore buffered output (works for both new and existing sessions)
		if(!_hasRestoredBuffer)
		{
			string bufferedOutput = TerminalService.GetBufferedOutput(SessionId);
			if(!string.IsNullOrEmpty(bufferedOutput))
			{
				try
				{
					await _terminal.Write(bufferedOutput);
					_hasRestoredBuffer = true;
				}
				catch { }
			}
		}

		_isSessionActive = true;

		await _terminal.Focus();
		await Resize();
	}

	DotNetObjectReference<TerminalPanel>? _dotNetRef;

	async Task OnTerminalFirstRender()
	{
		_dotNetRef = DotNetObjectReference.Create(this);
		await JS.InvokeVoidAsync("xtermInterop.registerWindowResize", _terminalId, _dotNetRef);
		await JS.InvokeVoidAsync("xtermInterop.observeElementResize", _terminalId, _dotNetRef);
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

		try
		{
			// IMPORTANT: Call fit() which will resize the terminal to match container
			await _terminal.Addon("addon-fit").InvokeVoidAsync("fit");

			// Small delay to let the fit operation complete
			await Task.Delay(100);

			await _terminal.Addon("addon-fit").InvokeVoidAsync("fit");

			// Query the actual cols/rows after fit
			TerminalSize? size = await JS.InvokeAsync<TerminalSize?>("xtermInterop.getTerminalSize", _terminalId);
			if(size is not null && size.cols > 0 && size.rows > 0)
			{
				// Resize the PTY to match the terminal's new dimensions
				TerminalService.ResizePty(SessionId, size.cols, size.rows);
			}
		}
		catch { }
	}

	class TerminalSize
	{
		public int cols { get; set; }
		public int rows { get; set; }
	}

	async Task OnTerminalInput(string data)
	{
		await TerminalService.WriteAsync(SessionId, data);
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
				catch { }
			});
		}
		catch(Exception ex)
		{
			// Log unhandled exceptions from async void to prevent app crash
			Logger.LogError(ex, "Unhandled exception in terminal data event handler for session {SessionId}", sessionId);
		}
	}

	protected virtual void Dispose(bool disposing)
	{
		if(!_disposed)
		{
			if(disposing)
			{
				_isSessionActive = false;
				TerminalService.OnDataReceived -= OnTerminalData;
				UIState.OnStateChanged -= OnUIStateChanged;
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
		TerminalService.SoftClear(SessionId);
		await TerminalService.RestartSession(SessionId, WorkingDirectory ?? Directory.GetCurrentDirectory());
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
