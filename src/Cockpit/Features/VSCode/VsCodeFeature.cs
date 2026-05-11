using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace Cockpit.Features.VSCode;

public sealed class VsCodeFeature
{
	readonly ILogger<VsCodeFeature> _logger;
	readonly IProcessLauncher _launcher;
	readonly string? _executablePath;

	public VsCodeFeature(ILogger<VsCodeFeature> logger)
	{
		_logger = logger;
		_launcher = new DefaultProcessLauncher();
		_executablePath = DetectVsCodePath();
		IsAvailable = _executablePath is not null;
	}

	/// <summary>Test seam: inject a known executable path and optional fake launcher.</summary>
	internal VsCodeFeature(ILogger<VsCodeFeature> logger, string? executablePath, IProcessLauncher? launcher = null)
	{
		_logger = logger;
		_launcher = launcher ?? new DefaultProcessLauncher();
		_executablePath = executablePath;
		IsAvailable = executablePath is not null;
	}

	public bool IsAvailable { get; }

	/// <summary>
	/// Returns the CLI arguments needed to open <paramref name="path"/> in VS Code.
	/// </summary>
	internal static IReadOnlyList<string> BuildOpenArguments(string path) => [path];

	/// <summary>
	/// Returns the CLI arguments needed to jump to <paramref name="line"/> in <paramref name="filePath"/> using VS Code's <c>--goto</c> flag.
	/// </summary>
	internal static IReadOnlyList<string> BuildGotoArguments(string filePath, int line) =>
		["--goto", $"{filePath}:{line}"];

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

			return p.ExitCode == 0 ? ResolveCommandFromPath("code") ?? "code" : null;
		}
		catch(Exception ex)
		{
			_logger.LogInformation(ex, "DetectVsCode: 'code --version' invocation failed, trying fallback paths");

			try
			{
				if(RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
				{
					string[] windowsCandidates =
					[
						Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft VS Code", "Code.exe"),
						Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Microsoft VS Code", "Code.exe"),
					];

					foreach(string candidate in windowsCandidates)
					{
						if(File.Exists(candidate))
						{
							return candidate;
						}
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

	static string? ResolveCommandFromPath(string command)
	{
		string? pathValue = Environment.GetEnvironmentVariable("PATH");
		if(pathValue is null)
		{
			return null;
		}

		string[] extensions = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
			? [".exe", ".cmd", ".bat"]
			: [""];

		foreach(string dir in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
		{
			foreach(string ext in extensions)
			{
				string candidate = Path.Combine(dir, command + ext);
				if(File.Exists(candidate))
				{
					return candidate;
				}
			}
		}

		return null;
	}

	/// <summary>
	/// Opens <paramref name="path"/> (file or directory) in VS Code.
	/// Returns <see langword="false"/> when VS Code is unavailable, the path is empty, or the launch fails.
	/// </summary>
	public bool OpenPathInVsCode(string path)
	{
		if(!IsAvailable || _executablePath is null)
		{
			_logger.LogWarning("OpenPathInVsCode called but VS Code is not available");
			return false;
		}

		if(string.IsNullOrWhiteSpace(path))
		{
			_logger.LogWarning("OpenPathInVsCode called with a null or empty path");
			return false;
		}

		try
		{
			ProcessStartInfo psi = new()
			{
				FileName = _executablePath,
				UseShellExecute = false,
				CreateNoWindow = false
			};

			foreach(string arg in BuildOpenArguments(path))
			{
				psi.ArgumentList.Add(arg);
			}

			using Process? p = _launcher.Start(psi);
			return p is not null;
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "OpenPathInVsCode failed for path {Path}", path);
			return false;
		}
	}

	/// <summary>
	/// Opens <paramref name="filePath"/> in VS Code and jumps to <paramref name="line"/> using the <c>--goto</c> flag.
	/// Returns <see langword="false"/> when VS Code is unavailable, the path is empty, the line is less than 1, or the launch fails.
	/// </summary>
	public bool OpenFileAtLineInVsCode(string filePath, int line)
	{
		if(!IsAvailable || _executablePath is null)
		{
			_logger.LogWarning("OpenFileAtLineInVsCode called but VS Code is not available");
			return false;
		}

		if(string.IsNullOrWhiteSpace(filePath))
		{
			_logger.LogWarning("OpenFileAtLineInVsCode called with a null or empty file path");
			return false;
		}

		if(line < 1)
		{
			_logger.LogWarning("OpenFileAtLineInVsCode called with an invalid line number {Line}", line);
			return false;
		}

		try
		{
			ProcessStartInfo psi = new()
			{
				FileName = _executablePath,
				UseShellExecute = false,
				CreateNoWindow = false
			};

			foreach(string arg in BuildGotoArguments(filePath, line))
			{
				psi.ArgumentList.Add(arg);
			}

			using Process? p = _launcher.Start(psi);
			return p is not null;
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "OpenFileAtLineInVsCode failed for {FilePath}:{Line}", filePath, line);
			return false;
		}
	}
}
