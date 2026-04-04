using System.Diagnostics;
using Cockpit.Features.Sessions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace Cockpit.Components.Pages.ContextPanel;

public partial class Directory : ComponentBase, IDisposable
{
	readonly SessionListFeature _sessionListFeature;
	readonly ILogger<Directory> _logger;

	public Directory(SessionListFeature sessionListFeature, ILogger<Directory> logger)
	{
		_sessionListFeature = sessionListFeature;
		_logger = logger;
	}

	string CurrentDirectory => _sessionListFeature.CurrentSession?.Context?.CurrentWorkingDirectory ?? string.Empty;
	string SessionDirectory => _sessionListFeature.CurrentSession?.Context?.WorkspacePath ?? string.Empty;

	string _renderedDirectory = string.Empty;
	string _renderedSessionDirectory = string.Empty;
	bool _hasRendered = false;

	protected override bool ShouldRender()
	{
		string current = CurrentDirectory;
		string session = SessionDirectory;
		if(_hasRendered && current == _renderedDirectory && session == _renderedSessionDirectory)
		{
			return false;
		}

		_hasRendered = true;
		_renderedDirectory = current;
		_renderedSessionDirectory = session;
		return true;
	}

	protected override void OnInitialized()
	{
		_sessionListFeature.OnStateChanged += OnStateChanged;
	}

	void OnStateChanged()
	{
		InvokeAsync(StateHasChanged);
	}

	public void Dispose()
	{
		Dispose(true);
		GC.SuppressFinalize(this);
	}

	protected virtual void Dispose(bool disposing)
	{
		if(disposing)
		{
			_sessionListFeature.OnStateChanged -= OnStateChanged;
		}
	}

	void OpenInExplorer(string path)
	{
		if(string.IsNullOrWhiteSpace(path))
		{
			return;
		}

		try
		{
			Process.Start(new ProcessStartInfo
			{
				FileName = path,
				UseShellExecute = true
			});
		}
		catch(Exception ex)
		{
			_logger?.LogWarning(ex, "Failed to open explorer for path {Path}", path);
		}
	}

    void OpenInVSCode(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        try
        {
            Process.Start(new ProcessStartInfo("code", $"\"{path}\"")
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to open VS Code for path {Path}", path);
        }
    }
}