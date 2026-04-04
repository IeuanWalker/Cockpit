using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace Cockpit.Features.VSCode;

public sealed class VsCodeFeature
{
	readonly ILogger<VsCodeFeature> _logger;
	readonly string? _executablePath;

	public VsCodeFeature(ILogger<VsCodeFeature> logger)
	{
		_logger = logger;
		_executablePath = DetectVsCodePath();
		IsAvailable = _executablePath is not null;
	}

	/// <summary>Test seam: inject a known executable path (or null to simulate unavailability).</summary>
	internal VsCodeFeature(ILogger<VsCodeFeature> logger, string? executablePath)
	{
		_logger = logger;
		_executablePath = executablePath;
		IsAvailable = executablePath is not null;
	}

	public bool IsAvailable { get; }

	string? DetectVsCodePath()
	{
		try
		{
			ProcessStartInfo psi = new()
			{
				FileName = "code",
				Arguments = "--version",
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
				CreateNoWindow = true
			};
			using Process? p = Process.Start(psi);

			if(p is null)
			{
				return null;
			}

			bool exited = p.WaitForExit(3000);
			if(!exited)
			{
				_logger.LogWarning("DetectVsCode: 'code --version' did not exit within the timeout");

				try
				{
					p.Kill(entireProcessTree: true);
					p.WaitForExit();
				}
				catch(Exception killEx)
				{
					_logger.LogDebug(killEx, "DetectVsCode: failed to terminate timed out 'code --version' process");
				}

				throw new TimeoutException("DetectVsCode: 'code --version' did not exit within the timeout.");
			}

			return p.ExitCode == 0 ? "code" : null;
		}
		catch(Exception ex)
		{
			_logger.LogInformation(ex, "DetectVsCode: 'code --version' invocation failed, trying fallback paths");

			try
			{
				if(RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
				{
					string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
					string exePath = Path.Combine(programFiles, "Microsoft VS Code", "Code.exe");
					if(File.Exists(exePath))
					{
						return exePath;
					}
				}
				else if(RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
				{
					const string macPath = "/Applications/Visual Studio Code.app/Contents/Resources/app/bin/code";
					if(File.Exists(macPath))
					{
						return macPath;
					}
				}
			}
			catch(Exception fallbackEx)
			{
				_logger.LogWarning(fallbackEx, "DetectVsCode: fallback path check failed");
			}

			return null;
		}
	}

	public bool OpenPathInVsCode(string path)
	{
		if(!IsAvailable || _executablePath is null)
		{
			_logger.LogWarning("OpenPathInVsCode called but VS Code is not available");
			return false;
		}

		try
		{
			ProcessStartInfo psi = new()
			{
				FileName = _executablePath,
				Arguments = $"\"{path}\"",
				UseShellExecute = true,
				CreateNoWindow = true
			};
			Process.Start(psi);
			return true;
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "OpenPathInVsCode failed for path {Path}", path);
			return false;
		}
	}
}
