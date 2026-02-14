using System.Text;
using Porta.Pty;

namespace Cockpit.Services;

public sealed partial class TerminalService : IDisposable
{
	readonly Dictionary<string, TerminalSession> _sessions = [];

	public event Action<string, string>? OnDataReceived;

	public async Task<bool> CreateSession(string sessionId, string workingDirectory)
	{
		if(_sessions.ContainsKey(sessionId))
		{
			return false;
		}

		try
		{
			PtyOptions options = new()
			{
				Cols = 120,
				Rows = 30,
				Cwd = workingDirectory,
				App = OperatingSystem.IsWindows() ? "powershell.exe" : "/bin/bash",
			};

			IPtyConnection ptyConnection = await PtyProvider.SpawnAsync(options, CancellationToken.None);
			TerminalSession session = new(sessionId, ptyConnection);

			// Start background task to read output
			_ = Task.Run(async () =>
			{
				var buffer = new byte[4096];
				try
				{
					while(true)
					{
						int bytesRead = await ptyConnection.ReaderStream.ReadAsync(buffer, 0, buffer.Length);
						if(bytesRead <= 0)
						{
							break;
						}

						string data = Encoding.UTF8.GetString(buffer, 0, bytesRead);
						session.BufferOutput(data);
						OnDataReceived?.Invoke(sessionId, data);
					}
				}
				catch(ObjectDisposedException) { }
				catch(Exception) { }
			});

			_sessions[sessionId] = session;

			return true;
		}
		catch
		{
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
		catch
		{
			return false;
		}
	}

	public void Dispose()
	{
		foreach(TerminalSession session in _sessions.Values)
		{
			try
			{
				session.Connection.Dispose();
			}
			catch { }
		}
		_sessions.Clear();
		GC.SuppressFinalize(this);
	}

	class TerminalSession
	{
		public string Id { get; }
		public IPtyConnection Connection { get; }
		readonly StringBuilder _outputBuffer = new();
		readonly Lock _bufferLock = new();

		public int Cols { get; set; } = 120;
		public int Rows { get; set; } = 30;

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
		if (_sessions.TryGetValue(sessionId, out TerminalSession? session))
		{
			session.Cols = cols;
			session.Rows = rows;
			session.Connection.Resize(cols, rows);
		}
	}
}
