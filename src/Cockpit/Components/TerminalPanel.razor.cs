using Cockpit.Services;
using Microsoft.AspNetCore.Components;
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

	private void OnUIStateChanged()
	{
		_ = InvokeAsync(Resize);
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
		bool isNewSession = await TerminalService.CreateSession(SessionId, workDir);

		// If session already existed, restore the buffered output
		if(!isNewSession && !_hasRestoredBuffer)
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

		await Resize();
	}

	DotNetObjectReference<TerminalPanel>? _dotNetRef;

	async Task OnTerminalFirstRender()
	{
		_dotNetRef = DotNetObjectReference.Create(this);
		await JS.InvokeVoidAsync("xtermInterop.registerWindowResize", _terminalId, _dotNetRef);
		await Resize();
	}

	[JSInvokable]
	public async Task OnTerminalWindowResize()
	{
		await Resize();
	}

	async Task OnTerminalResize()
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
			// Fit terminal to container
			await _terminal.Addon("addon-fit").InvokeVoidAsync("fit");

			// Give time for fit to apply
			await Task.Delay(10);

			// Query cols/rows from JS interop
			var size = await JS.InvokeAsync<TerminalSize?>("xtermInterop.getTerminalSize", _terminalId);
			if (size != null && size.cols > 0 && size.rows > 0)
			{
				// Subtract 4 from cols to avoid wrapping due to rounding/padding/scrollbar
				int safeCols = size.cols > 1 ? size.cols - 1 : size.cols;
				TerminalService.ResizePty(SessionId, safeCols, size.rows);
			}
		}
		catch { }
	}

	private class TerminalSize
	{
		public int cols { get; set; }
		public int rows { get; set; }
	}

	async Task OnTerminalInput(string data)
	{
		await TerminalService.WriteAsync(SessionId, data);
	}

	async void OnTerminalData(string sessionId, string data)
	{
		if(sessionId != SessionId || _terminal is null || !_isSessionActive)
		{
			return;
		}

		await InvokeAsync(async () =>
		{
			try
			{
				await _terminal.Write(data);
			}
			catch { }
		});
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

		await _terminal.Reset();
	}

	public void Dispose()
	{
		Dispose(true);
		GC.SuppressFinalize(this);
	}

}