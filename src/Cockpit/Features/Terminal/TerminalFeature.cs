using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging;
using Porta.Pty;

namespace Cockpit.Features.Terminal;

public sealed partial class TerminalFeature : IDisposable
{
	readonly ILogger<TerminalFeature> _logger;
	readonly ConcurrentDictionary<string, TerminalSessionModel> _sessions = new();

	public event Action<string, string>? OnDataReceived;

	public TerminalFeature(ILogger<TerminalFeature> logger)
	{
		_logger = logger;
	}

	public async Task<bool> CreateSession(string sessionId, string workingDirectory)
	{
		try
		{
			PtyOptions options = new()
			{
				Cols = 120,
				Rows = 30,
				Cwd = workingDirectory,
				// NOTE: Shell application is currently fixed; update here if user override is added.
				App = OperatingSystem.IsWindows() ? "powershell.exe" : "/bin/bash",
			};

			IPtyConnection ptyConnection = await PtyProvider.SpawnAsync(options, CancellationToken.None);
			TerminalSessionModel session = new(sessionId, ptyConnection);

			// Create cancellation token for the background read task
			CancellationTokenSource cts = new();
			session.ReadTaskCancellation = cts;

			// Start background task to read output
			session.ReadTask = Task.Run(async () =>
			{
				byte[] buffer = new byte[4096];
				try
				{
					while(!cts.Token.IsCancellationRequested)
					{
						int bytesRead = await ptyConnection.ReaderStream.ReadAsync(buffer, cts.Token);
						if(bytesRead <= 0)
						{
							break;
						}

						string data = Encoding.UTF8.GetString(buffer, 0, bytesRead);
						session.BufferOutput(data);
						OnDataReceived?.Invoke(sessionId, data);
					}
				}
				catch(Exception ex)
				{
					_logger.LogError(ex, "Terminal session {SessionId} background task failed", sessionId);
				}
			}, cts.Token);

			// Try to add the session; if it already exists, dispose the new connection
			if(!_sessions.TryAdd(sessionId, session))
			{
				cts.Cancel();
				cts.Dispose();
				ptyConnection.Dispose();
				return false;
			}

			return true;
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Failed to create terminal session {SessionId}", sessionId);
			return false;
		}
	}

	public string GetBufferedOutput(string sessionId)
	{
		if(_sessions.TryGetValue(sessionId, out TerminalSessionModel? session))
		{
			return session.GetBuffer();
		}
		return string.Empty;
	}

	public async Task<bool> WriteAsync(string sessionId, string data)
	{
		if(!_sessions.TryGetValue(sessionId, out TerminalSessionModel? session))
		{
			return false;
		}

		try
		{
			byte[] bytes = Encoding.UTF8.GetBytes(data);
			await session.Connection.WriterStream.WriteAsync(bytes);
			await session.Connection.WriterStream.FlushAsync();
			return true;
		}
		catch(Exception ex)
		{
			_logger.LogDebug(ex, "Failed to write to terminal session {SessionId}", sessionId);
			return false;
		}
	}

	public void Dispose()
	{
		foreach(TerminalSessionModel session in _sessions.Values)
		{
			// Cancel the background read task
			session.ReadTaskCancellation?.Cancel();

			try
			{
				// Wait for the task to complete (it should exit quickly due to cancellation)
				session.ReadTask?.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
			}
			catch(Exception ex)
			{
				_logger.LogDebug(ex, "Failed to wait for read task to complete for session {SessionId}", session.Id);
			}

			session.ReadTaskCancellation?.Dispose();

			try
			{
				session.Connection.Dispose();
			}
			catch(Exception ex)
			{
				_logger.LogDebug(ex, "Failed to dispose terminal session {SessionId} during cleanup", session.Id);
			}
		}
		_sessions.Clear();
		GC.SuppressFinalize(this);
	}

	public void ResizePty(string sessionId, int cols, int rows)
	{
		if(_sessions.TryGetValue(sessionId, out TerminalSessionModel? session))
		{
			session.Cols = cols;
			session.Rows = rows;
			session.Connection.Resize(cols, rows);
		}
	}

	public async Task RestartSession(string sessionId, string workingDirectory)
	{
		// Dispose existing session if present
		if(_sessions.TryRemove(sessionId, out TerminalSessionModel? existing))
		{
			// Cancel the background read task
			existing.ReadTaskCancellation?.Cancel();

			try
			{
				// Wait for the task to complete
				if(existing.ReadTask is not null)
				{
					await existing.ReadTask.WaitAsync(TimeSpan.FromSeconds(2));
				}
			}
			catch(Exception ex)
			{
				_logger.LogDebug(ex, "Failed to wait for read task to complete during restart for session {SessionId}", sessionId);
			}

			existing.ReadTaskCancellation?.Dispose();

			// Prevent old output from reappearing after restart
			existing.ClearBuffer();
			try
			{
				existing.Connection.Dispose();
			}
			catch(Exception ex)
			{
				_logger.LogDebug(ex, "Failed to dispose existing terminal connection during restart for session {SessionId}", sessionId);
			}
		}

		// Create a fresh session
		await CreateSession(sessionId, workingDirectory);
	}

	public void SoftClear(string sessionId)
	{
		if(_sessions.TryGetValue(sessionId, out TerminalSessionModel? session))
		{
			session.ClearBuffer();
		}
	}

	public async Task CloseSession(string sessionId)
	{
		if(_sessions.TryRemove(sessionId, out TerminalSessionModel? session))
		{
			// Cancel the background read task
			session.ReadTaskCancellation?.Cancel();

			try
			{
				// Wait for the task to complete
				if(session.ReadTask is not null)
				{
					await session.ReadTask.WaitAsync(TimeSpan.FromSeconds(2));
				}
			}
			catch(Exception ex)
			{
				_logger.LogDebug(ex, "Failed to wait for read task to complete during close for session {SessionId}", sessionId);
			}

			session.ReadTaskCancellation?.Dispose();

			try
			{
				session.Connection.Dispose();
			}
			catch(Exception ex)
			{
				_logger.LogDebug(ex, "Failed to dispose terminal session {SessionId} during close", sessionId);
			}
		}
	}
}
