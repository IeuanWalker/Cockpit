using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging;
using Porta.Pty;

namespace Cockpit.Services;

public sealed partial class TerminalService : IDisposable
{
	readonly ILogger<TerminalService> _logger;
	readonly ConcurrentDictionary<string, TerminalSession> _sessions = new();

	public event Action<string, string>? OnDataReceived;

	public TerminalService(ILogger<TerminalService> logger)
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
			TerminalSession session = new(sessionId, ptyConnection);

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
		if(_sessions.TryGetValue(sessionId, out TerminalSession? session))
		{
			return session.GetBuffer();
		}
		return string.Empty;
	}

	public async Task<bool> WriteAsync(string sessionId, string data)
	{
		if(!_sessions.TryGetValue(sessionId, out TerminalSession? session))
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
		foreach(TerminalSession session in _sessions.Values)
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

	/// <summary>
	/// Represents a single terminal session managed by <see cref="TerminalService"/>.
	/// Maintains the underlying PTY connection and a bounded in-memory output buffer
	/// for streaming and retrieval of terminal data.
	/// </summary>
	class TerminalSession
	{
		const int maxBufferSize = 1024 * 1024; // 1MB limit
		public string Id { get; }
		public IPtyConnection Connection { get; }
		readonly StringBuilder _outputBuffer = new();
		readonly Lock _bufferLock = new();

		public int Cols { get; set; } = 120;
		public int Rows { get; set; } = 30;

		public CancellationTokenSource? ReadTaskCancellation { get; set; }
		public Task? ReadTask { get; set; }

		public TerminalSession(string id, IPtyConnection connection)
		{
			Id = id;
			Connection = connection;
		}

		public void BufferOutput(string data)
		{
			lock(_bufferLock)
			{
				_outputBuffer.Append(data);

				// Trim buffer if it exceeds max size
				if(_outputBuffer.Length > maxBufferSize)
				{
					int excessLength = _outputBuffer.Length - maxBufferSize;
					_outputBuffer.Remove(0, excessLength);
				}
			}
		}

		public string GetBuffer()
		{
			lock(_bufferLock)
			{
				return _outputBuffer.ToString();
			}
		}

		public void ClearBuffer()
		{
			lock(_bufferLock)
			{
				_outputBuffer.Clear();
			}
		}
	}

	public void ResizePty(string sessionId, int cols, int rows)
	{
		if(_sessions.TryGetValue(sessionId, out TerminalSession? session))
		{
			session.Cols = cols;
			session.Rows = rows;
			session.Connection.Resize(cols, rows);
		}
	}

	public async Task RestartSession(string sessionId, string workingDirectory)
	{
		// Dispose existing session if present
		if(_sessions.TryRemove(sessionId, out TerminalSession? existing))
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
		if(_sessions.TryGetValue(sessionId, out TerminalSession? session))
		{
			session.ClearBuffer();
		}
	}

	public void CloseSession(string sessionId)
	{
		if(_sessions.TryRemove(sessionId, out TerminalSession? session))
		{
			// Cancel the background read task
			session.ReadTaskCancellation?.Cancel();

			try
			{
				// Wait for the task to complete
				session.ReadTask?.WaitAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
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
