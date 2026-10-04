using System.Text;
using System.Text.Json;
using Cockpit.Tooling;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Cockpit.Tools.Terminal;

public sealed class TerminalToolInstance : WorkspaceToolInstance<TerminalState>
{
	readonly WorkspaceToolInstanceContext _context;
	readonly ITerminalToolBackend _backend;
	readonly object _outputLock = new();
	readonly StringBuilder _pendingOutput = new();
	readonly SemaphoreSlim _rendererGate = new(1, 1);
	IJSObjectReference? _renderer;
	DotNetObjectReference<TerminalToolInstance>? _reference;
	bool _subscribed, _disposed;
	int _flushing;
	int _checkpointScheduled;
	public TerminalToolInstance(WorkspaceToolInstanceContext context, ITerminalToolBackend backend)
		: base(context.InstanceId, TerminalJsonContext.Default.TerminalState) { _context = context; _backend = backend; }
	public string RuntimeId => $"{_context.Session.RuntimeSessionKey.Value}:{InstanceId.Value}";
	public string Output => _backend.GetBufferedOutput(RuntimeId);
	public string? Error { get; private set; }
	public event Action? Changed;
	protected override int SchemaVersion => TerminalState.CurrentSchemaVersion;
	protected override TerminalState CreateDefaultState() => new() { WorkingDirectory = _context.Session.WorkingDirectory };
	TerminalLaunch Launch(int columns, int rows) => new(_context.Session.RuntimeSessionKey.Value, InstanceId.Value,
		State.WorkingDirectory ?? _context.Session.WorkingDirectory ?? Directory.GetCurrentDirectory(), columns, rows);

	// Own the renderer for the lifetime of the tab, independently of Razor mount/unmount.
	public async Task AttachAsync(IJSRuntime js, ElementReference element, string attachment)
	{
		await _rendererGate.WaitAsync();
		try
		{
			if(_disposed) return;
			if(_renderer is null)
			{
				_renderer = await js.InvokeAsync<IJSObjectReference>("import", "./_content/Cockpit.Tools.Terminal/terminal.js");
				_reference = DotNetObjectReference.Create(this);
			}
			await _renderer.InvokeVoidAsync("attach", RuntimeId, element, attachment, _reference,
				State.Transcript, State.Columns, State.Rows, _backend.WindowsBuildNumber);
		}
		finally { _rendererGate.Release(); }
	}
	public async Task DetachAsync(string attachment)
	{
		if(_renderer is not null && !_disposed)
			try { await _renderer.InvokeVoidAsync("detach", RuntimeId, attachment); }
			catch(JSDisconnectedException) { }
	}
	public Task StartAsync(CancellationToken cancellationToken = default) => StartAsync(State.Columns, State.Rows, cancellationToken);
	async Task StartAsync(int columns, int rows, CancellationToken cancellationToken)
	{
		if(_disposed) return;
		if(!_subscribed) { _backend.OutputReceived += OnOutput; _subscribed = true; }
		TerminalLaunch launch = Launch(columns, rows);
		bool started = await _backend.EnsureStartedAsync(RuntimeId, launch, cancellationToken);
		SetState(State with { WorkingDirectory = launch.WorkingDirectory, WasRunning = started, Columns = columns, Rows = rows });
		if(!started) throw new InvalidOperationException("The terminal shell could not be started. Use Reset to retry.");
	}
	[JSInvokable] public Task Start(int columns, int rows) => StartAsync(columns, rows, _context.LifetimeToken);
	[JSInvokable] public Task Input(string input) => SendAsync(input);
	public async Task SendAsync(string input, CancellationToken cancellationToken = default)
	{
		if(!_disposed && !await _backend.WriteAsync(RuntimeId, input, cancellationToken))
			throw new InvalidOperationException("The shell is no longer accepting input. Use Reset to start a new shell.");
	}
	[JSInvokable] public async Task Resize(int columns, int rows)
	{
		if(_disposed || columns < 2 || rows < 1) return;
		await _backend.ResizeAsync(RuntimeId, columns, rows, _context.LifetimeToken);
		SetState(State with { Columns = columns, Rows = rows });
	}
	[JSInvokable] public string TakeOutput()
	{
		lock(_outputLock) { string output = _pendingOutput.ToString(); _pendingOutput.Clear(); return output; }
	}
	[JSInvokable] public async Task Restart(int columns, int rows)
	{
		await _backend.CloseAsync(RuntimeId);
		TakeOutput();
		SetState(State with { Transcript = string.Empty });
		await StartAsync(columns, rows, _context.LifetimeToken);
	}
	[JSInvokable] public void ReportError(string message) { if(!_disposed) { Error = message; Changed?.Invoke(); } }
	public async Task ResetAsync()
	{
		Error = null;
		Changed?.Invoke();
		if(_renderer is not null) await _renderer.InvokeVoidAsync("reset", RuntimeId);
	}
	public async Task<string> GetTextAsync(int lines) => _renderer is null ? string.Empty :
        await _renderer.InvokeAsync<string>("messageText", RuntimeId, Math.Clamp(lines, 1, 10000));
	public ValueTask<WorkspaceHostCommandResult> AddToMessageAsync(string text) =>
		_context.Host.AppendToComposerAsync($"Terminal output:\n````\n{text}\n````", _context.LifetimeToken);
	public async Task FocusAsync()
	{
		if(_renderer is not null && !_disposed) await _renderer.InvokeVoidAsync("focus", RuntimeId);
	}
	public override async ValueTask OnActivatedAsync(WorkspaceToolActivationContext context, CancellationToken cancellationToken)
	{
		// Start only once the visible surface has supplied real dimensions.
		await FocusAsync();
	}
	public override async ValueTask<ToolCheckpoint> CaptureAsync(ToolStateWriteContext destination, CancellationToken cancellationToken)
	{
		await _rendererGate.WaitAsync(cancellationToken);
		try
		{
			string transcript = State.Transcript;
			if(_renderer is not null && !_disposed)
				transcript = await _renderer.InvokeAsync<string>("snapshot", cancellationToken, RuntimeId);
			TerminalState snapshot = State with { Transcript = transcript, WasRunning = _backend.IsRunning(RuntimeId) };
			return new(TerminalState.CurrentSchemaVersion, MutationVersion,
				JsonSerializer.SerializeToElement(snapshot, TerminalJsonContext.Default.TerminalState), []);
		}
		finally { _rendererGate.Release(); }
	}
	public override ValueTask<ToolCloseDecision> CanCloseAsync(ToolCloseReason reason, CancellationToken cancellationToken) =>
		ValueTask.FromResult(reason == ToolCloseReason.UserClosedTab && _backend.IsRunning(RuntimeId)
			? ToolCloseDecision.Confirm("This terminal is still running. Closing the tab will stop its shell and any active command.", "Close terminal")
			: ToolCloseDecision.Allow());
	void OnOutput(string id, string output)
	{
		if(id != RuntimeId || _disposed) return;
		lock(_outputLock) _pendingOutput.Append(output);
		_ = FlushAsync();
		if(Interlocked.Exchange(ref _checkpointScheduled, 1) == 0) _ = MarkOutputChangedAsync();
	}
	async Task MarkOutputChangedAsync()
	{
		try
		{
			await Task.Delay(1000, _context.LifetimeToken);
			if(!_disposed) SetState(State);
		}
		catch(OperationCanceledException) { }
		finally { Interlocked.Exchange(ref _checkpointScheduled, 0); }
	}
	async Task FlushAsync()
	{
		if(_renderer is null || Interlocked.Exchange(ref _flushing, 1) != 0) return;
		try { await _renderer.InvokeVoidAsync("flush", RuntimeId); }
		catch(JSDisconnectedException) { return; }
		catch(Exception ex) { ReportError(ex.Message); }
		finally { Interlocked.Exchange(ref _flushing, 0); }
		bool pending;
		lock(_outputLock) pending = _pendingOutput.Length > 0;
		if(pending && !_disposed && Error is null) _ = FlushAsync();
	}
	public override async ValueTask DisposeAsync()
	{
		_disposed = true;
		if(_subscribed) _backend.OutputReceived -= OnOutput;
		await _backend.CloseAsync(RuntimeId);
		await _rendererGate.WaitAsync();
		try
		{
			if(_renderer is not null)
			{
				try { await _renderer.InvokeVoidAsync("dispose", RuntimeId); await _renderer.DisposeAsync(); }
				catch(JSDisconnectedException) { }
			}
			_reference?.Dispose();
		}
		finally { _rendererGate.Release(); }
	}
}
