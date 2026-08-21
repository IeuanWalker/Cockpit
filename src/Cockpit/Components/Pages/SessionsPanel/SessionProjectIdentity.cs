using Cockpit.Features.Sessions.Models;

namespace Cockpit.Components.Pages.SessionsPanel;

internal sealed record SessionProjectIdentity(
	string Id,
	string RootId,
	string? RepositoryId,
	string RootPath,
	string BaseName,
	string? Repository);

internal static class SessionProjectIdentityResolver
{
	internal static StringComparer PathComparer { get; } = OperatingSystem.IsWindows()
		? StringComparer.OrdinalIgnoreCase
		: StringComparer.Ordinal;
	internal static StringComparer ProjectIdComparer => PathComparer;

	public static SessionProjectIdentity? Resolve(SessionModel session)
	{
		if(string.IsNullOrWhiteSpace(session.Context.CurrentWorkingDirectory))
		{
			return null;
		}

		string workingDirectory = session.Context.CurrentWorkingDirectory;
		string rootPath = NormalizePath(workingDirectory);
		if(!string.IsNullOrWhiteSpace(session.Context.GitRoot) && TryNormalizePath(session.Context.GitRoot, out string? normalizedGitRoot))
		{
			rootPath = normalizedGitRoot;
		}

		string baseName = GetBaseName(rootPath);
		string identityPath = OperatingSystem.IsWindows() ? rootPath.ToUpperInvariant() : rootPath;
		string rootId = $"path:{identityPath}";
		string? repositoryId = NormalizeRepositoryId(session.Context.Repository);
		if(repositoryId is not null)
		{
			string repositoryName = session.Context.Repository!.Trim().Replace('\\', '/').Trim('/');
			if(repositoryName.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
			{
				repositoryName = repositoryName[..^4];
			}
			baseName = repositoryName[(repositoryName.LastIndexOf('/') + 1)..];
		}

		return new SessionProjectIdentity(
			repositoryId is null ? rootId : $"repo:{repositoryId}",
			rootId,
			repositoryId is null ? null : $"repo:{repositoryId}",
			rootPath,
			string.IsNullOrWhiteSpace(baseName) ? rootPath : baseName,
			session.Context.Repository);
	}

	static string? NormalizeRepositoryId(string? repository)
	{
		if(string.IsNullOrWhiteSpace(repository))
		{
			return null;
		}

		string normalized = repository.Trim().Replace('\\', '/').Trim('/');
		if(normalized.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
		{
			normalized = normalized[..^4];
		}

		// A bare repository name is not globally unique, so keep using its Git root.
		if(!normalized.Contains('/', StringComparison.Ordinal))
		{
			return null;
		}

		return normalized.ToUpperInvariant();
	}

	internal static string NormalizePath(string path)
	{
		if(TryNormalizePath(path, out string? normalizedPath))
		{
			return normalizedPath;
		}

		// Retain a deterministic identity for historical paths that cannot be made absolute.
		return TrimEndingDirectorySeparator(NormalizeSeparators(path));
	}

	static bool TryNormalizePath(string path, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? normalizedPath)
	{
		try
		{
			normalizedPath = TrimEndingDirectorySeparator(Path.GetFullPath(NormalizeSeparators(path)));
			return true;
		}
		catch(ArgumentException)
		{
		}
		catch(NotSupportedException)
		{
		}
		catch(IOException)
		{
		}

		normalizedPath = null;
		return false;
	}

	static string NormalizeSeparators(string path) => path.Trim().Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

	static string TrimEndingDirectorySeparator(string path)
	{
		try
		{
			string? root = Path.GetPathRoot(path);
			if(root is not null && PathComparer.Equals(Path.TrimEndingDirectorySeparator(path), Path.TrimEndingDirectorySeparator(root)))
			{
				return Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
			}
		}
		catch(ArgumentException)
		{
			// Invalid historical paths still receive the deterministic trimming below.
		}

		return Path.TrimEndingDirectorySeparator(path);
	}

	internal static string GetBaseName(string path)
	{
		try
		{
			return Path.GetFileName(path);
		}
		catch(ArgumentException)
		{
			int separatorIndex = Math.Max(path.LastIndexOf(Path.DirectorySeparatorChar), path.LastIndexOf(Path.AltDirectorySeparatorChar));
			return separatorIndex < 0 ? path : path[(separatorIndex + 1)..];
		}
	}
}
