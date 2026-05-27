using Microsoft.Extensions.Logging;

namespace Cockpit.Features.FileSearch;

public sealed class FileSearchFeature : IFileSearchFeature
{
	readonly ILogger<FileSearchFeature> _logger;

	// Directories to skip entirely (never recurse into)
	static readonly HashSet<string> skipDirs = new(StringComparer.OrdinalIgnoreCase)
	{
		".git", "node_modules", "bin", "obj", ".vs", ".vscode", ".idea",
		"__pycache__", ".next", "dist", "build", "out", ".cache", "coverage",
		"packages", ".nuget"
	};

	public FileSearchFeature(ILogger<FileSearchFeature> logger)
	{
		_logger = logger;
	}

	public Task<IReadOnlyList<FileSearchResult>> SearchAsync(string workingDirectory, string filter, int maxResults = int.MaxValue, CancellationToken cancellationToken = default)
	{
		return Task.Run<IReadOnlyList<FileSearchResult>>(() => Search(workingDirectory, filter, maxResults, cancellationToken), cancellationToken);
	}

	List<FileSearchResult> Search(string workingDirectory, string filter, int maxResults, CancellationToken cancellationToken)
	{
		if(maxResults <= 0)
		{
			return [];
		}

		if(string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
		{
			return [];
		}

		try
		{
			List<FileSearchResult> results = [];
			EnumerateFiles(workingDirectory, workingDirectory, filter, results, maxResults, cancellationToken);
			SortResults(results, filter);
			if(results.Count > maxResults)
			{
				results.RemoveRange(maxResults, results.Count - maxResults);
			}
			return results;
		}
		catch(OperationCanceledException)
		{
			throw;
		}
		catch(Exception ex)
		{
			_logger.LogDebug(ex, "File search failed in {Directory}", workingDirectory);
			return [];
		}
	}

	// Pre-computes per-result sort keys to avoid repeated ToLowerInvariant() and depth counting
	// inside each comparison during sort. Results are bounded at maxResults (≤ 200 by default).
	static void SortResults(List<FileSearchResult> results, string filter)
	{
		if(results.Count <= 1)
		{
			return;
		}

		string lowerFilter = filter.ToLowerInvariant();

		(FileSearchResult Result, string LowerName, int Depth)[] keyed = new (FileSearchResult, string, int)[results.Count];
		for(int i = 0; i < results.Count; i++)
		{
			FileSearchResult r = results[i];
			keyed[i] = (r, r.FileName.ToLowerInvariant(), CountPathDepth(r.RelativePath));
		}

		// Sort: prefix matches first, then by depth (shallower first), then alphabetically
		Array.Sort(keyed, (a, b) =>
		{
			bool aPrefix = a.LowerName.StartsWith(lowerFilter, StringComparison.Ordinal);
			bool bPrefix = b.LowerName.StartsWith(lowerFilter, StringComparison.Ordinal);
			if(aPrefix != bPrefix)
			{
				return aPrefix ? -1 : 1;
			}
			if(a.Depth != b.Depth)
			{
				return a.Depth.CompareTo(b.Depth);
			}
			return string.Compare(a.Result.RelativePath, b.Result.RelativePath, StringComparison.OrdinalIgnoreCase);
		});

		results.Clear();
		foreach((FileSearchResult result, string _, int _) in keyed)
		{
			results.Add(result);
		}
	}

	static int CountPathDepth(string relativePath)
	{
		int count = 0;
		foreach(char c in relativePath)
		{
			if(c == Path.DirectorySeparatorChar || c == '/')
			{
				count++;
			}
		}
		return count;
	}

	void EnumerateFiles(string root, string dir, string filter, List<FileSearchResult> results, int maxResults, CancellationToken cancellationToken)
	{
		if(results.Count >= maxResults)
		{
			return;
		}

		cancellationToken.ThrowIfCancellationRequested();

		try
		{
			foreach(string filePath in Directory.EnumerateFiles(dir))
			{
				cancellationToken.ThrowIfCancellationRequested();

				string fileName = Path.GetFileName(filePath);
				if(string.IsNullOrEmpty(filter) || fileName.Contains(filter, StringComparison.OrdinalIgnoreCase))
				{
					string relativePath = Path.GetRelativePath(root, filePath);
					results.Add(new FileSearchResult(fileName, relativePath, filePath));
					if(results.Count >= maxResults)
					{
						return;
					}
				}
			}

			foreach(string subDir in Directory.EnumerateDirectories(dir))
			{
				if(results.Count >= maxResults)
				{
					return;
				}

				cancellationToken.ThrowIfCancellationRequested();

				string dirName = Path.GetFileName(subDir);
				if(skipDirs.Contains(dirName))
				{
					continue;
				}

				if(string.IsNullOrEmpty(filter) || dirName.Contains(filter, StringComparison.OrdinalIgnoreCase))
				{
					string relativeDirPath = Path.GetRelativePath(root, subDir);
					results.Add(new FileSearchResult(dirName, relativeDirPath, subDir, IsDirectory: true));
					if(results.Count >= maxResults)
					{
						return;
					}
				}

				EnumerateFiles(root, subDir, filter, results, maxResults, cancellationToken);
			}
		}
		catch(OperationCanceledException)
		{
			throw;
		}
		catch(UnauthorizedAccessException)
		{
			// Skip inaccessible directories silently
		}
		catch(Exception ex)
		{
			_logger.LogDebug(ex, "Could not enumerate {Directory}", dir);
		}
	}
}
