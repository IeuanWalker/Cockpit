using System.Diagnostics;
using System.Text;
using Cockpit.Features.Git.Models;
using Microsoft.Extensions.Logging;

namespace Cockpit.Features.Git;

public sealed partial class GitFeature
{
	readonly ILogger<GitFeature> _logger;
	readonly Func<string, string[], Task<string?>> _runCommand;

	public GitFeature(ILogger<GitFeature> logger)
		: this(logger, RunGitAsync)
	{
	}

	// Internal constructor used by unit tests to inject a fake command runner.
	internal GitFeature(ILogger<GitFeature> logger, Func<string, string[], Task<string?>> runCommand)
	{
		_logger = logger;
		_runCommand = runCommand;
	}

	Task<string?> RunCommand(string workingDirectory, params string[] arguments) =>
		_runCommand(workingDirectory, arguments);

	static async Task<string?> RunGitAsync(string workingDirectory, string[] arguments)
	{
		try
		{
			ProcessStartInfo psi = new("git")
			{
				WorkingDirectory = workingDirectory,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
				CreateNoWindow = true,
				StandardOutputEncoding = Encoding.UTF8
			};

			foreach(string arg in arguments)
			{
				psi.ArgumentList.Add(arg);
			}

			using Process process = Process.Start(psi)!;
			Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
			Task<string> stderrTask = process.StandardError.ReadToEndAsync();
			await Task.WhenAll(stdoutTask, stderrTask, process.WaitForExitAsync());

			return process.ExitCode == 0 ? stdoutTask.Result.TrimEnd() : null;
		}
		catch
		{
			return null;
		}
	}

	/// <summary>
	/// Resolves git context (root, repository, branch) for a given working directory.
	/// Returns a <see cref="GitContext"/> with null fields if the directory is not a git repo.
	/// </summary>
	public async Task<GitContext?> GetContext(string? workingDirectory)
	{
		if(string.IsNullOrWhiteSpace(workingDirectory))
		{
			return null;
		}

		if(!Directory.Exists(workingDirectory))
		{
			return null;
		}

		try
		{
			Task<string?> rootTask = RunCommand(workingDirectory, "rev-parse", "--show-toplevel");
			Task<string?> branchTask = RunCommand(workingDirectory, "rev-parse", "--abbrev-ref", "HEAD");
			Task<string?> remoteTask = RunCommand(workingDirectory, "remote", "get-url", "origin");

			await Task.WhenAll(rootTask, branchTask, remoteTask);

			string? gitRoot = string.IsNullOrEmpty(rootTask.Result) ? rootTask.Result : Path.GetFullPath(rootTask.Result);

			string? remoteUrl = remoteTask.Result;
			string? repository = null;
			if(!string.IsNullOrEmpty(remoteUrl))
			{
				string lastSegment = remoteUrl.TrimEnd('/').Split('/').Last().Split(':').Last();
				repository = Path.GetFileNameWithoutExtension(lastSegment);
			}

			return new GitContext { GitRoot = gitRoot, Repository = repository, Branch = branchTask.Result };
		}
		catch(Exception ex)
		{
			_logger.LogWarning(ex, "Failed to get git context for {WorkingDirectory}", workingDirectory);
			return new GitContext();
		}
	}

	/// <summary>
	/// Returns the current branch name for a git repository, or null if not in a repo.
	/// Returns "HEAD" when in detached-HEAD state.
	/// </summary>
	public Task<string?> GetBranch(string gitRoot) => RunCommand(gitRoot, "rev-parse", "--abbrev-ref", "HEAD");

	/// <summary>
	/// Returns the list of changed files (staged + unstaged) in a git repository.
	/// </summary>
	public async Task<List<GitChangedFileModel>> GetChangedFiles(string gitRoot)
	{
		try
		{
			string? output = await RunCommand(gitRoot, "status", "--porcelain");
			if(string.IsNullOrEmpty(output))
			{
				return [];
			}

			// First pass: resolve all file paths and statuses (expand untracked directories).
			List<(string FilePath, GitFileStatusEnum Status)> fileEntries = ParsePorcelainOutput(output, gitRoot);

			// Second pass: fetch all diffs concurrently with bounded parallelism.
			using SemaphoreSlim semaphore = new(8);
			GitChangedFileModel?[] results = await Task.WhenAll(
				fileEntries.Select(async e =>
				{
					await semaphore.WaitAsync();
					try
					{
						return await BuildFileModelAsync(gitRoot, e.FilePath, e.Status);
					}
					finally
					{
						semaphore.Release();
					}
				}));

			return [.. results.OfType<GitChangedFileModel>()];
		}
		catch(Exception ex)
		{
			_logger.LogWarning(ex, "Failed to get changed files for {GitRoot}", gitRoot);
			return [];
		}
	}

	/// <summary>
	/// Parses <c>git status --porcelain</c> output into (filePath, status) pairs.
	/// Untracked directories are expanded to their constituent files.
	/// </summary>
	internal static List<(string FilePath, GitFileStatusEnum Status)> ParsePorcelainOutput(string output, string gitRoot)
	{
		List<(string, GitFileStatusEnum)> entries = [];

		foreach(string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
		{
			if(line.Length < 4)
			{
				continue;
			}

			char staged = line[0];
			char unstaged = line[1];
			string path = UnescapeGitPath(line[3..].Trim());

			if(staged == '?' && unstaged == '?')
			{
				// Untracked directory — recurse into it.
				if(path.EndsWith('/'))
				{
					string fullDir = Path.Combine(gitRoot, path);
					if(Directory.Exists(fullDir))
					{
						foreach(string file in Directory.EnumerateFiles(fullDir, "*", SearchOption.AllDirectories))
						{
							entries.Add((Path.GetRelativePath(gitRoot, file), GitFileStatusEnum.Untracked));
						}
					}
				}
				else
				{
					entries.Add((path, GitFileStatusEnum.Untracked));
				}
				continue;
			}

			// Handle renames: "old.txt -> new.txt" — take the new name.
			if(path.Contains(" -> "))
			{
				path = path.Split(" -> ")[1];
			}

			GitFileStatusEnum status = ClassifyStatus(staged, unstaged);
			entries.Add((path, status));
		}

		return entries;
	}

	/// <summary>
	/// Maps a pair of git porcelain status characters to a <see cref="GitFileStatusEnum"/>.
	/// </summary>
	internal static GitFileStatusEnum ClassifyStatus(char staged, char unstaged) =>
		(staged, unstaged) switch
		{
			('A', _) => GitFileStatusEnum.Added,
			('D', _) or (_, 'D') => GitFileStatusEnum.Deleted,
			('R', _) => GitFileStatusEnum.Renamed,
			_ => GitFileStatusEnum.Modified
		};

	/// <summary>
	/// Strips the surrounding double-quotes from a git C-quoted path and unescapes the contents.
	/// Paths without quotes are returned unchanged.
	/// </summary>
	static string UnescapeGitPath(string raw)
	{
		if(raw.Length < 2 || raw[0] != '"' || raw[^1] != '"')
		{
			return raw;
		}

		string inner = raw[1..^1];
		StringBuilder sb = new(inner.Length);
		int i = 0;
		while(i < inner.Length)
		{
			if(inner[i] != '\\' || i + 1 >= inner.Length)
			{
				sb.Append(inner[i++]);
				continue;
			}

			char esc = inner[++i];
			switch(esc)
			{
				case 'n': sb.Append('\n'); i++; break;
				case 't': sb.Append('\t'); i++; break;
				case '"': sb.Append('"'); i++; break;
				case '\\': sb.Append('\\'); i++; break;
				default:
					// Octal escape: \NNN
					if(esc >= '0' && esc <= '7' && i + 2 < inner.Length)
					{
						int octal = (esc - '0') * 64 + (inner[i + 1] - '0') * 8 + (inner[i + 2] - '0');
						sb.Append((char)octal);
						i += 3;
					}
					else
					{
						sb.Append('\\');
						sb.Append(esc);
						i++;
					}
					break;
			}
		}

		return sb.ToString();
	}

	async Task<GitChangedFileModel?> BuildFileModelAsync(string gitRoot, string filePath, GitFileStatusEnum status)
	{
		try
		{
			string? diff = await GetDiffAsync(gitRoot, filePath, status);
			return new GitChangedFileModel
			{
				Name = Path.GetFileName(filePath),
				Path = filePath,
				Status = status,
				Diff = diff
			};
		}
		catch
		{
			return null;
		}
	}

	/// <summary>
	/// Watches a git repository for file changes and invokes <paramref name="onChanged"/> when changes are detected.
	/// Dispose the returned handle to stop watching.
	/// </summary>
	public IDisposable Watch(string gitRoot, Func<Task> onChanged)
	{
		FileSystemWatcher watcher = new(gitRoot)
		{
			IncludeSubdirectories = true,
			NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName
			// EnableRaisingEvents is set after handlers are attached to avoid a race where
			// FS events could fire before any handler is registered.
		};

		// Debounce to avoid flooding callbacks on bulk changes.
		System.Timers.Timer debounce = new(300)
		{
			AutoReset = false
		};

		int running = 0;
		int pending = 0;
		debounce.Elapsed += async (_, _) =>
		{
			if(Interlocked.CompareExchange(ref running, 1, 0) != 0)
			{
				// Another execution is in progress; record that new events arrived so
				// it will re-run after finishing rather than silently dropping them.
				Interlocked.Exchange(ref pending, 1);
				return;
			}

			do
			{
				Interlocked.Exchange(ref pending, 0);
				try
				{
					await onChanged();
				}
				catch(Exception ex)
				{
					_logger.LogError(ex, "GitFeature.Watch callback exception");
				}
			}
			while(Interlocked.CompareExchange(ref pending, 0, 0) == 1);

			Interlocked.Exchange(ref running, 0);
		};

		watcher.Changed += OnFsEvent;
		watcher.Created += OnFsEvent;
		watcher.Deleted += OnFsEvent;
		// RenamedEventArgs derives from FileSystemEventArgs so the same handler works.
		watcher.Renamed += (_, e) => OnFsEvent(null!, e);
		// Start raising events only after all handlers are attached.
		watcher.EnableRaisingEvents = true;

		return new GitWatcher(watcher, debounce);

		void OnFsEvent(object _, FileSystemEventArgs e)
		{
			// Ignore internal git object churn.
			if(e.FullPath.Contains(Path.DirectorySeparatorChar + ".git" + Path.DirectorySeparatorChar + "objects"))
			{
				return;
			}

			debounce.Stop();
			debounce.Start();
		}
	}

	async Task<string?> GetDiffAsync(string gitRoot, string filePath, GitFileStatusEnum status)
	{
		string fullPath = Path.Combine(gitRoot, filePath);

		if(status == GitFileStatusEnum.Untracked)
		{
			return await GetUntrackedFileDiffAsync(gitRoot, filePath);
		}

		// For Added files (staged but never committed), git diff HEAD fails because HEAD
		// has no record of the file. Try --cached (index vs HEAD) first, then fall back
		// to reading the file directly.
		if(status == GitFileStatusEnum.Added)
		{
			string? cached = await RunCommand(gitRoot, "diff", "--cached", "--", fullPath);
			if(!string.IsNullOrEmpty(cached))
			{
				return cached;
			}

			return await GetUntrackedFileDiffAsync(gitRoot, filePath);
		}

		// working-tree vs index (unstaged changes); if nothing unstaged, try index vs HEAD (staged-only).
		string? diff = await RunCommand(gitRoot, "diff", "--", fullPath);
		if(!string.IsNullOrEmpty(diff))
		{
			return diff;
		}

		return await RunCommand(gitRoot, "diff", "--cached", "--", fullPath);
	}

	async Task<string?> GetUntrackedFileDiffAsync(string gitRoot, string filePath)
	{
		try
		{
			string fullPath = Path.Combine(gitRoot, filePath);
			string content = await File.ReadAllTextAsync(fullPath);
			// Trim trailing newline so Split doesn't produce a spurious empty element,
			// which would make the hunk header count wrong and emit a lone "+" line.
			string trimmed = content.TrimEnd('\n');
			string[] lines = trimmed.Length == 0 ? [] : trimmed.Split('\n');
			StringBuilder sb = new();
			sb.AppendLine("--- /dev/null");
			sb.AppendLine($"+++ b/{filePath}");
			sb.AppendLine($"@@ -0,0 +1,{lines.Length} @@");
			foreach(string line in lines)
			{
				sb.Append('+');
				sb.AppendLine(line);
			}

			return sb.ToString();
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
