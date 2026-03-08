using System.Net.Http.Json;
using System.Text.Json;

namespace Cockpit.Features.Updates;

public record UpdateCheckResult(bool UpdateAvailable, string CurrentVersion, GitHubRelease? LatestRelease);

public record GitHubRelease(
	string TagName,
	string Name,
	string Body,
	bool IsPrerelease,
	bool IsDraft,
	DateTime PublishedAt,
	string HtmlUrl,
	IReadOnlyList<GitHubReleaseAsset> Assets);

public record GitHubReleaseAsset(string Name, string DownloadUrl, long Size);

public class UpdateFeature : IDisposable
{
	static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);

	readonly HttpClient _httpClient;
	readonly string _currentVersion;
	readonly string _apiUrl;
	readonly Timer _timer;

	UpdateCheckResult? _cachedResult;
	string? _dismissedVersion;

	public UpdateCheckResult? CachedResult => _cachedResult;
	public string? DismissedVersion => _dismissedVersion;
	public string CurrentVersion => _currentVersion;

	public event Action? OnUpdateChecked;

	public UpdateFeature()
	{
		_httpClient = new HttpClient();
		_httpClient.DefaultRequestHeaders.Add("User-Agent", "Cockpit");
		_currentVersion = Microsoft.Maui.ApplicationModel.AppInfo.VersionString;
		_apiUrl = "https://api.github.com/repos/IeuanWalker/Cockpit/releases";

		// Check immediately on startup, then every hour
		_timer = new Timer(_ => _ = CheckForUpdateAsync(), null, TimeSpan.Zero, CheckInterval);
	}

	public void DismissVersion(string version) => _dismissedVersion = version;

	public async Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken cancellationToken = default)
	{
		try
		{
			var releases = await GetReleasesAsync(cancellationToken);

			var latest = releases
				.Where(r => !r.IsPrerelease && !r.IsDraft)
				.OrderByDescending(r => r.PublishedAt)
				.FirstOrDefault();

			var result = new UpdateCheckResult(
				latest != null && IsNewerVersion(latest.TagName, _currentVersion),
				_currentVersion,
				latest);

			_cachedResult = result;
			OnUpdateChecked?.Invoke();
			return result;
		}
		catch
		{
			return new UpdateCheckResult(false, _currentVersion, null);
		}
	}

	async Task<IReadOnlyList<GitHubRelease>> GetReleasesAsync(CancellationToken cancellationToken)
	{
		var options = new JsonSerializerOptions
		{
			PropertyNameCaseInsensitive = true,
			PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
		};

		var dtos = await _httpClient.GetFromJsonAsync<List<GitHubReleaseDto>>(_apiUrl, options, cancellationToken);
		if (dtos is null)
			return [];

		return dtos.Select(r => new GitHubRelease(
			r.TagName ?? "",
			r.Name ?? r.TagName ?? "",
			r.Body ?? "",
			r.Prerelease,
			r.Draft,
			r.PublishedAt ?? DateTime.MinValue,
			r.HtmlUrl ?? "",
			(r.Assets ?? []).Select(a => new GitHubReleaseAsset(
				a.Name ?? "",
				a.BrowserDownloadUrl ?? "",
				a.Size)).ToList()
		)).ToList();
	}

	internal static bool IsNewerVersion(string remoteVersion, string currentVersion)
	{
		var remote = remoteVersion.TrimStart('v');
		var current = currentVersion.TrimStart('v');

		try
		{
			var remoteNums = remote.Split(['-', '+'])[0].Split('.')
				.Select(p => int.TryParse(p, out var n) ? n : -1)
				.TakeWhile(n => n >= 0)
				.ToList();

			var currentNums = current.Split(['-', '+'])[0].Split('.')
				.Select(p => int.TryParse(p, out var n) ? n : -1)
				.TakeWhile(n => n >= 0)
				.ToList();

			for (int i = 0; i < Math.Max(remoteNums.Count, currentNums.Count); i++)
			{
				var r = i < remoteNums.Count ? remoteNums[i] : 0;
				var c = i < currentNums.Count ? currentNums[i] : 0;
				if (r > c) return true;
				if (r < c) return false;
			}

			return false;
		}
		catch
		{
			return string.Compare(remote, current, StringComparison.OrdinalIgnoreCase) > 0;
		}
	}

	public void OpenReleaseInBrowser(GitHubRelease release) =>
		_ = Microsoft.Maui.ApplicationModel.Launcher.Default.OpenAsync(new Uri(release.HtmlUrl));

	public void Dispose()
	{
		Dispose(true);
		GC.SuppressFinalize(this);
	}

	protected virtual void Dispose(bool disposing)
	{
		if (disposing)
		{
			_timer.Dispose();
			_httpClient.Dispose();
		}
	}
}

internal class GitHubReleaseDto
{
	public string? TagName { get; set; }
	public string? Name { get; set; }
	public string? Body { get; set; }
	public bool Prerelease { get; set; }
	public bool Draft { get; set; }
	public DateTime? PublishedAt { get; set; }
	public string? HtmlUrl { get; set; }
	public List<GitHubReleaseAssetDto>? Assets { get; set; }
}

internal class GitHubReleaseAssetDto
{
	public string? Name { get; set; }
	public string? BrowserDownloadUrl { get; set; }
	public long Size { get; set; }
}
