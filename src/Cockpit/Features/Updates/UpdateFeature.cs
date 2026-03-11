using System.Net.Http.Json;
using System.Text.Json;
using Cockpit.Features.Updates.Models;

namespace Cockpit.Features.Updates;

public sealed partial class UpdateFeature : IDisposable
{
	static readonly TimeSpan checkInterval = TimeSpan.FromHours(1);
	const string apiUrl = "https://api.github.com/repos/IeuanWalker/Cockpit/releases";

	readonly HttpClient _httpClient;
	readonly string _currentVersion;
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
		_currentVersion = AppInfo.VersionString;

		// Check immediately on startup, then every hour
		_timer = new Timer(_ => _ = CheckForUpdateAsync(), null, TimeSpan.Zero, checkInterval);
	}

	public void DismissVersion(string version) => _dismissedVersion = version;

	public async Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken cancellationToken = default)
	{
		try
		{
			IReadOnlyList<GitHubReleaseModel> releases = await GetReleasesAsync(cancellationToken);

			GitHubReleaseModel? latest = releases
				.Where(r => r is { Prerelease: false, Draft: false })
				.OrderByDescending(r => r.PublishedAt)
				.FirstOrDefault();

			if(latest?.TagName is null)
			{
				return new UpdateCheckResult(false, _currentVersion, null);
			}

			UpdateCheckResult result = new(
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

	async Task<IReadOnlyList<GitHubReleaseModel>> GetReleasesAsync(CancellationToken cancellationToken)
	{
		JsonSerializerOptions options = new()
		{
			PropertyNameCaseInsensitive = true,
			PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
		};

		List<GitHubReleaseModel>? dtos = await _httpClient.GetFromJsonAsync<List<GitHubReleaseModel>>(apiUrl, options, cancellationToken);
		if(dtos is null)
		{
			return [];
		}

		return dtos;
	}

	internal static bool IsNewerVersion(string remoteVersion, string currentVersion)
	{
		string remote = remoteVersion.TrimStart('v');
		string current = currentVersion.TrimStart('v');

		try
		{
			List<int> remoteNums = [.. remote.Split(['-', '+'])[0].Split('.')
				.Select(p => int.TryParse(p, out int n) ? n : -1)
				.TakeWhile(n => n >= 0)];

			List<int> currentNums = [.. current.Split(['-', '+'])[0].Split('.')
				.Select(p => int.TryParse(p, out int n) ? n : -1)
				.TakeWhile(n => n >= 0)];

			for(int i = 0; i < Math.Max(remoteNums.Count, currentNums.Count); i++)
			{
				int r = i < remoteNums.Count ? remoteNums[i] : 0;
				int c = i < currentNums.Count ? currentNums[i] : 0;
				if(r > c)
				{
					return true;
				}

				if(r < c)
				{
					return false;
				}
			}

			return false;
		}
		catch
		{
			return string.Compare(remote, current, StringComparison.OrdinalIgnoreCase) > 0;
		}
	}

	public void OpenReleaseInBrowser(GitHubReleaseModel release) => _ = Launcher.Default.OpenAsync(new Uri(release.HtmlUrl));

	public void Dispose()
	{
		Dispose(true);
		GC.SuppressFinalize(this);
	}

	void Dispose(bool disposing)
	{
		if(disposing)
		{
			_timer.Dispose();
			_httpClient.Dispose();
		}
	}
}