namespace Cockpit.Features.FileSearch;

public interface IFileSearchFeature
{
	/// <summary>
	/// Searches for files and directories in the given working directory matching the filter.
	/// Returns paths relative to <paramref name="workingDirectory"/>, sorted by relevance:
	/// prefix matches appear before contains-matches, shallower paths before deeper ones.
	/// </summary>
	/// <param name="workingDirectory">Absolute path of the directory to search.</param>
	/// <param name="filter">Substring to match against file and directory names (case-insensitive). Pass an empty string to return all files and directories.</param>
	/// <param name="maxResults">Maximum number of results to return. Defaults to 50.</param>
	/// <param name="cancellationToken">Token to cancel the operation.</param>
	Task<IReadOnlyList<FileSearchResult>> SearchAsync(string workingDirectory, string filter, int maxResults = 50, CancellationToken cancellationToken = default);
}

public record FileSearchResult(
	string FileName,
	string RelativePath,
	string FullPath,
	bool IsDirectory = false
);
