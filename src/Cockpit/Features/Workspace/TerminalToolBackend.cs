using System.Collections.Concurrent;
using Cockpit.Features.Terminal;
using Cockpit.Tools.Terminal;

namespace Cockpit.Features.Workspace;

sealed class TerminalToolBackend : ITerminalToolBackend
{
	readonly TerminalFeature _terminal;
	readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.Ordinal);
	public TerminalToolBackend(TerminalFeature terminal)
	{
		_terminal = terminal;
		_terminal.OnDataReceived += (id, data) => OutputReceived?.Invoke(id, data);
	}
	public event Action<string, string>? OutputReceived;
	public bool IsSupported => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() || OperatingSystem.IsLinux();
	public int? WindowsBuildNumber => OperatingSystem.IsWindows() ? Environment.OSVersion.Version.Build : null;
	public bool IsRunning(string runtimeId) => _terminal.GetSession(runtimeId) is not null;
	public string GetBufferedOutput(string runtimeId) => _terminal.GetBufferedOutput(runtimeId);
	public async Task<bool> EnsureStartedAsync(string runtimeId, TerminalLaunch launch, CancellationToken cancellationToken)
	{
		SemaphoreSlim gate = _gates.GetOrAdd(runtimeId, _ => new(1, 1));
		await gate.WaitAsync(cancellationToken);
		try
		{
			return _terminal.GetSession(runtimeId) is not null ||
				await _terminal.CreateSession(runtimeId, launch.WorkingDirectory, launch.Columns, launch.Rows, cancellationToken);
		}
		finally { gate.Release(); }
	}
	public Task<bool> WriteAsync(string runtimeId, string input, CancellationToken cancellationToken) => _terminal.WriteAsync(runtimeId, input, cancellationToken);
	public async Task ResizeAsync(string runtimeId, int columns, int rows, CancellationToken cancellationToken)
	{
		SemaphoreSlim gate = _gates.GetOrAdd(runtimeId, _ => new(1, 1));
		await gate.WaitAsync(cancellationToken);
		try { _terminal.ResizePty(runtimeId, columns, rows); }
		finally { gate.Release(); }
	}
	public async Task CloseAsync(string runtimeId)
	{
		SemaphoreSlim gate = _gates.GetOrAdd(runtimeId, _ => new(1, 1));
		await gate.WaitAsync();
		try { await _terminal.CloseSessionAsync(runtimeId); }
		finally { gate.Release(); }
	}
}
