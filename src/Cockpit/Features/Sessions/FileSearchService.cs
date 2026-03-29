using Microsoft.Extensions.Logging;

namespace Cockpit.Features.Sessions;

public class FileSearchService : IFileSearchService
{
	readonly ILogger<FileSearchService> _logger;

	// Directories to skip entirely (never recurse into)
	static readonly HashSet<string> skipDirs = new(StringComparer.OrdinalIgnoreCase)
	{
		".git", "node_modules", "bin", "obj", ".vs", ".vscode", ".idea",
		"__pycache__", ".next", "dist", "build", "out", ".cache", "coverage",
		"packages", ".nuget"
	};

	public FileSearchService(ILogger<FileSearchService> logger)
	{
		_logger = logger;
	}

	public Task<IReadOnlyList<FileSearchResult>> SearchAsync(string workingDirectory, string filter, int maxResults = 50)
	{
		return Task.Run(() => Search(workingDirectory, filter, maxResults));
	}

	IReadOnlyList<FileSearchResult> Search(string workingDirectory, string filter, int maxResults)
	{
		if(string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
		{
			return [];
		}

		try
		{
			List<FileSearchResult> results = [];
			EnumerateFiles(workingDirectory, workingDirectory, filter, results, maxResults);

			// Sort: exact filename prefix matches first, then contains matches, then by path length
			string lowerFilter = filter.ToLowerInvariant();
			results.Sort((a, b) =>
			{
				string aName = a.FileName.ToLowerInvariant();
				string bName = b.FileName.ToLowerInvariant();
				bool aPrefix = aName.StartsWith(lowerFilter);
				bool bPrefix = bName.StartsWith(lowerFilter);
				if(aPrefix != bPrefix)
				{
					return aPrefix ? -1 : 1;
				}
				// Within same tier, sort by path depth (shallower first) then alphabetically
				int aDepth = a.RelativePath.Count(c => c == Path.DirectorySeparatorChar || c == '/');
				int bDepth = b.RelativePath.Count(c => c == Path.DirectorySeparatorChar || c == '/');
				if(aDepth != bDepth)
				{
					return aDepth.CompareTo(bDepth);
				}

				return string.Compare(a.RelativePath, b.RelativePath, StringComparison.OrdinalIgnoreCase);
			});

			return results;
		}
		catch(Exception ex)
		{
			_logger.LogDebug(ex, "File search failed in {Directory}", workingDirectory);
			return [];
		}
	}

	void EnumerateFiles(string root, string dir, string filter, List<FileSearchResult> results, int maxResults)
	{
		if(results.Count >= maxResults)
		{
			return;
		}

		try
		{
			foreach(string filePath in Directory.EnumerateFiles(dir))
			{
				if(results.Count >= maxResults)
				{
					return;
				}

				string fileName = Path.GetFileName(filePath);
				if(string.IsNullOrEmpty(filter) || fileName.Contains(filter, StringComparison.OrdinalIgnoreCase))
				{
					string relativePath = Path.GetRelativePath(root, filePath);
					results.Add(new FileSearchResult(fileName, relativePath, filePath));
				}
			}

			foreach(string subDir in Directory.EnumerateDirectories(dir))
			{
				if(results.Count >= maxResults)
				{
					return;
				}

				string dirName = Path.GetFileName(subDir);
				if(skipDirs.Contains(dirName))
				{
					continue;
				}

				EnumerateFiles(root, subDir, filter, results, maxResults);
			}
		}
		catch(UnauthorizedAccessException)
		{
			// Skip inaccessible directories
		}
		catch(Exception ex)
		{
			_logger.LogDebug(ex, "Could not enumerate {Directory}", dir);
		}
	}
}
