using System.Text;
using Porta.Pty;

namespace Cockpit.Features.Terminal;

/// <summary>
/// Represents a single terminal session managed by <see cref="TerminalFeature"/>.
/// Maintains the underlying PTY connection and a bounded in-memory output buffer
/// for streaming and retrieval of terminal data.
/// </summary>
class TerminalSessionModel
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

	public TerminalSessionModel(string id, IPtyConnection connection)
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
