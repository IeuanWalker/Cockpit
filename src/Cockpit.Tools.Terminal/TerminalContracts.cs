namespace Cockpit.Tools.Terminal;

public sealed record TerminalLaunch(string RuntimeSessionKey, string InstanceId, string WorkingDirectory, int Columns, int Rows);

public interface ITerminalToolBackend
{
	event Action<string, string>? OutputReceived;
	bool IsSupported { get; }
	int? WindowsBuildNumber { get; }
	bool IsRunning(string runtimeId);
	string GetBufferedOutput(string runtimeId);
	Task<bool> EnsureStartedAsync(string runtimeId, TerminalLaunch launch, CancellationToken cancellationToken);
	Task<bool> WriteAsync(string runtimeId, string input, CancellationToken cancellationToken);
	Task CloseAsync(string runtimeId);
	Task ResizeAsync(string runtimeId, int columns, int rows, CancellationToken cancellationToken);
}
