using System.Diagnostics;
using Cockpit.Features.Git.Models;
using Microsoft.Extensions.Logging;

namespace Cockpit.Features.Git;

public class GitFeature
{
	readonly ILogger<GitFeature> _logger;
	public GitFeature(ILogger<GitFeature> logger)
	{
		_logger = logger;
	}

	/// <summary>
	/// Resolves git context (root, repository, branch) for a given working directory.
	/// Returns a <see cref="GitContext"/> with null fields if the directory is not a git repo.
	/// </summary>
	public async Task<GitContext> GetContextAsync(string workingDirectory)
	{
		try
		{
			string? gitRoot = await RunCommandAsync(workingDirectory, "rev-parse --show-toplevel");
			string? branch = await RunCommandAsync(workingDirectory, "rev-parse --abbrev-ref HEAD");
			string? remoteUrl = await RunCommandAsync(workingDirectory, "remote get-url origin");

			string? repository = null;
			if(!string.IsNullOrEmpty(remoteUrl))
			{
				// Handle both HTTPS (https://github.com/owner/repo.git) and SSH (git@github.com:owner/repo.git)
				string lastSegment = remoteUrl.TrimEnd('/').Split('/').Last().Split(':').Last();
				repository = Path.GetFileNameWithoutExtension(lastSegment);
			}

			return new GitContext { GitRoot = gitRoot, Repository = repository, Branch = branch };
		}
		catch(Exception ex)
		{
			_logger.LogWarning(ex, "Failed to get git context for {WorkingDirectory}", workingDirectory);
			return new GitContext();
		}
	}

	/// <summary>
	/// Returns the list of changed files (staged + unstaged) in a git repository.
	/// </summary>
	public async Task<List<GitChangedFileModel>> GetChangedFilesAsync(string gitRoot)
	{
		List<GitChangedFileModel> results = [];

		try
		{
			string? output = await RunCommandAsync(gitRoot, "status --porcelain");
			if(string.IsNullOrEmpty(output))
			{
				return results;
			}

			foreach(string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
			{
				if(line.Length < 4)
				{
					continue;
				}

				char staged = line[0];
				char unstaged = line[1];
				string filePath = line[3..].Trim();

				// Handle renames: "old.txt -> new.txt" — take the new name
				if(filePath.Contains(" -> "))
				{
					filePath = filePath.Split(" -> ")[1];
				}

				GitFileStatus status = (staged, unstaged) switch
				{
					('?', '?') => GitFileStatus.Untracked,
					('A', _) => GitFileStatus.Added,
					('D', _) or (_, 'D') => GitFileStatus.Deleted,
					('R', _) => GitFileStatus.Renamed,
					_ => GitFileStatus.Modified
				};

				results.Add(new GitChangedFileModel
				{
					Name = Path.GetFileName(filePath),
					Path = filePath,
					Status = status
				});
			}
		}
		catch(Exception ex)
		{
			_logger.LogWarning(ex, "Failed to get changed files for {GitRoot}", gitRoot);
		}

		return results;
	}

	/// <summary>
	/// Watches a git repository for file changes and invokes <paramref name="onChanged"/> when changes are detected.
	/// Dispose the returned handle to stop watching.
	/// </summary>
	public IDisposable Watch(string gitRoot, Action onChanged)
	{
		FileSystemWatcher watcher = new(gitRoot)
		{
			IncludeSubdirectories = true,
			NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,
			EnableRaisingEvents = true
		};

		// Debounce to avoid flooding callbacks on bulk changes
		System.Timers.Timer debounce = new(300) { AutoReset = false };
		debounce.Elapsed += (_, _) => onChanged();

		FileSystemEventHandler handler = (_, e) =>
		{
			// Ignore internal git object churn
			if(e.FullPath.Contains(Path.DirectorySeparatorChar + ".git" + Path.DirectorySeparatorChar + "objects"))
			{
				return;
			}

			debounce.Stop();
			debounce.Start();
		};

		watcher.Changed += handler;
		watcher.Created += handler;
		watcher.Deleted += handler;
		watcher.Renamed += (_, e) =>
		{
			debounce.Stop();
			debounce.Start();
		};

		return new GitWatcher(watcher, debounce);
	}

	async Task<string?> RunCommandAsync(string workingDirectory, string arguments)
	{
		try
		{
			ProcessStartInfo psi = new("git", arguments)
			{
				WorkingDirectory = workingDirectory,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
				CreateNoWindow = true
			};

			using Process process = Process.Start(psi)!;
			string output = await process.StandardOutput.ReadToEndAsync();
			await process.WaitForExitAsync();

			return process.ExitCode == 0 ? output.Trim() : null;
		}
		catch
		{
			return null;
		}
	}

	sealed partial class GitWatcher : IDisposable
	{
		readonly FileSystemWatcher _watcher;
		readonly System.Timers.Timer _debounce;
		public GitWatcher(FileSystemWatcher watcher, System.Timers.Timer debounce)
		{
			_watcher = watcher;
			_debounce = debounce;
		}
		public void Dispose()
		{
			_watcher.Dispose();
			_debounce.Dispose();
		}
	}
}
