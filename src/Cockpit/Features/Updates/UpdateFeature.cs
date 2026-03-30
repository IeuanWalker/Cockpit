using System.Net.Http.Json;
using System.Text.Json;
using Cockpit.Features.Updates.Models;

namespace Cockpit.Features.Updates;

public sealed partial class UpdateFeature : IDisposable
{
	static readonly TimeSpan checkInterval = TimeSpan.FromHours(1);
	const string latestReleaseUrl = "https://api.github.com/repos/IeuanWalker/Cockpit/releases/latest";

	readonly HttpClient _httpClient;
	readonly string _currentVersion;
	readonly string _currentBuildNumber;
	Timer? _timer;

	UpdateCheckResult? _cachedResult;
	string? _dismissedVersion;
	DateTime? _lastChecked;

	public UpdateCheckResult? CachedResult => _cachedResult;
	public string? DismissedVersion => _dismissedVersion;
	public string CurrentVersion => _currentVersion;

	public string BuildNumber => _currentBuildNumber;
	public DateTime? LastChecked => _lastChecked;
	public DateTime? InstalledDate { get; }

	public event Action? OnUpdateChecked;

	public UpdateFeature(HttpClient httpClient)
	{
		_httpClient = httpClient;
		_currentVersion = AppInfo.VersionString;
		_currentBuildNumber = AppInfo.BuildString;

		string key = $"installed_date_{_currentVersion}";
		if(VersionTracking.Default.IsFirstLaunchForCurrentVersion)
		{
			Preferences.Default.Set(key, DateTime.UtcNow);
		}

		DateTime? installedDate = null;
		if(Preferences.Default.ContainsKey(key))
		{
			DateTime stored = Preferences.Default.Get(key, DateTime.MinValue);
			installedDate = stored == DateTime.MinValue ? null : stored;
		}

		InstalledDate = installedDate;
	}

	/// <summary>
	/// Starts the periodic update check. Call once after the application has started.
	/// </summary>
	public void Initialize()
	{
		_timer = new Timer(_ => _ = CheckForUpdate(), null, TimeSpan.Zero, checkInterval);
	}

	public void DismissVersion(string version) => _dismissedVersion = version;

	public async Task<UpdateCheckResult> CheckForUpdate(CancellationToken cancellationToken = default)
	{
		try
		{
			GitHubReleaseModel? latest = await GetLatestRelease(cancellationToken);

			if(latest?.TagName is null)
			{
				_lastChecked = DateTime.UtcNow;
				OnUpdateChecked?.Invoke();
				return new UpdateCheckResult(false, _currentVersion, null);
			}

			UpdateCheckResult result = new(
				IsNewerVersion(latest.TagName, _currentVersion),
				_currentVersion,
				latest);

			_cachedResult = result;
			_lastChecked = DateTime.UtcNow;
			OnUpdateChecked?.Invoke();
			return result;
		}
		catch
		{
			_lastChecked = DateTime.UtcNow;
			OnUpdateChecked?.Invoke();
			return new UpdateCheckResult(false, _currentVersion, null);
		}
	}

	async Task<GitHubReleaseModel?> GetLatestRelease(CancellationToken cancellationToken)
	{
		JsonSerializerOptions options = new()
		{
			PropertyNameCaseInsensitive = true,
			PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
		};

		return await _httpClient.GetFromJsonAsync<GitHubReleaseModel>(latestReleaseUrl, options, cancellationToken);
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

	public void OpenReleaseInBrowser(GitHubReleaseModel release)
	{
		if(release is null)
		{
			return;
		}

		if(string.IsNullOrWhiteSpace(release.HtmlUrl))
		{
			return;
		}

		if(!Uri.TryCreate(release.HtmlUrl, UriKind.Absolute, out Uri? uri))
		{
			return;
		}

		_ = Launcher.Default.OpenAsync(uri);
	}
	public void Dispose()
	{
		Dispose(true);
		GC.SuppressFinalize(this);
	}

	void Dispose(bool disposing)
	{
		if(disposing)
		{
			_timer?.Dispose();
			_httpClient.Dispose();
		}
	}
}