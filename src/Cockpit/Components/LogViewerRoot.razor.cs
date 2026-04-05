using Cockpit.Utilities.Logging;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Cockpit.Components;

public sealed partial class LogViewerRoot : ComponentBase, IDisposable
{
	[Inject] IJSRuntime JSRuntime { get; set; } = default!;

	ElementReference _logContainer;

	string _activeFile = "app.log";
	string _logContent = string.Empty;
	bool _autoScroll = true;
	bool _pendingScroll;

	CancellationTokenSource? _cts;

	protected override void OnInitialized()
	{
		ReadContent();
		_pendingScroll = true;
		StartPolling();
	}

	void SwitchFile(string fileName)
	{
		_activeFile = fileName;
		ReadContent();
		_pendingScroll = true;
		StateHasChanged();
	}

	void RefreshNow()
	{
		ReadContent();
		_pendingScroll = true;
		StateHasChanged();
	}

	void ReadContent()
	{
		string path = Path.Combine(LogDirectoryHelper.LogDirectory, _activeFile);
		try
		{
			if (!File.Exists(path))
			{
				_logContent = string.Empty;
				return;
			}

			using FileStream fs = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
			using StreamReader sr = new(fs);
			_logContent = sr.ReadToEnd();
		}
		catch
		{
			_logContent = string.Empty;
		}
	}

	void StartPolling()
	{
		_cts?.Cancel();
		_cts?.Dispose();
		_cts = new CancellationTokenSource();
		CancellationToken token = _cts.Token;

		_ = Task.Run(async () =>
		{
			using PeriodicTimer timer = new(TimeSpan.FromSeconds(1));
			while (!token.IsCancellationRequested && await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
			{
				string path = Path.Combine(LogDirectoryHelper.LogDirectory, _activeFile);
				string newContent;
				try
				{
					if (!File.Exists(path))
					{
						newContent = string.Empty;
					}
					else
					{
						using FileStream fs = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
						using StreamReader sr = new(fs);
						newContent = sr.ReadToEnd();
					}
				}
				catch { continue; }

				if (newContent != _logContent)
				{
					_logContent = newContent;
					if (_autoScroll)
					{
						_pendingScroll = true;
					}

					await InvokeAsync(StateHasChanged).ConfigureAwait(false);
				}
			}
		}, token);
	}

	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		if (_pendingScroll && _autoScroll)
		{
			_pendingScroll = false;
			try
			{
				await JSRuntime.InvokeVoidAsync("cockpit.scrollElementToBottom", _logContainer);
			}
			catch { /* best-effort */ }
		}
	}

	public void Dispose()
	{
		_cts?.Cancel();
		_cts?.Dispose();
		_cts = null;
	}
}
