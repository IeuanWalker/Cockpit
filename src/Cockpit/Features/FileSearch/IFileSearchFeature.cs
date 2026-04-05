namespace Cockpit.Features.FileSearch;

public interface IFileSearchFeature
{
	/// <summary>
	/// Searches for files in the given working directory matching the filter.
	/// Returns file paths relative to workingDirectory, sorted by relevance.
	/// </summary>
	Task<List<FileSearchResult>> SearchAsync(string workingDirectory, string filter, int maxResults = 50, CancellationToken cancellationToken = default);
}

public record FileSearchResult(
	string FileName,
	string RelativePath,
	string FullPath
);
