using System.Net.Http.Json;
using System.Text.Json;
using Cockpit.Features.Updates.Models;

namespace Cockpit.Features.Updates;

public sealed partial class UpdateFeature : IDisposable
{
	static readonly TimeSpan checkInterval = TimeSpan.FromHours(1);
	const string latestReleaseUrl = "https://api.github.com/repos/IeuanWalker/Cockpit/releases/latest";

	static readonly JsonSerializerOptions releaseJsonOptions = new()
	{
		PropertyNameCaseInsensitive = true,
		PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
	};

	readonly HttpClient _httpClient;
	readonly string _currentVersion;
	readonly CancellationTokenSource _cts = new();
	readonly SemaphoreSlim _checkLock = new(1, 1);
	Task? _checkTask;

	UpdateCheckResult? _cachedResult;
	string? _dismissedVersion;
	DateTime? _lastChecked;
	bool _disposed;

	public UpdateCheckResult? CachedResult => _cachedResult;
	public string? DismissedVersion => _dismissedVersion;
	public string CurrentVersion => _currentVersion;

	public DateTime? LastChecked => _lastChecked;
	public DateTime? InstalledDate { get; }

	public event Action? OnUpdateChecked;

	public UpdateFeature(HttpClient httpClient)
	{
		_httpClient = httpClient;
		_currentVersion = AppInfo.VersionString;

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
	/// Test-only constructor. Skips MAUI runtime calls.
	/// </summary>
	internal UpdateFeature(HttpClient httpClient, string currentVersion)
	{
		_httpClient = httpClient;
		_currentVersion = currentVersion;
		InstalledDate = null;
	}

	/// <summary>
	/// Starts the periodic update check. Call once after the application has started.
	/// Subsequent calls are no-ops.
	/// </summary>
	public void Initialize()
	{
		if(_checkTask is not null)
		{
			return;
		}

		_checkTask = RunPeriodicCheckAsync(_cts.Token);
	}

	async Task RunPeriodicCheckAsync(CancellationToken cancellationToken)
	{
		try
		{
			await CheckForUpdate(cancellationToken).ConfigureAwait(false);

			using PeriodicTimer timer = new(checkInterval);
			while(await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
			{
				await CheckForUpdate(cancellationToken).ConfigureAwait(false);
			}
		}
		catch(Exception)
		{
			// Intentionally swallow exceptions to avoid crashing the app. The next periodic check will try again.
		}
	}

	public void DismissVersion(string version) => _dismissedVersion = version;

	public async Task<UpdateCheckResult> CheckForUpdate(CancellationToken cancellationToken = default)
	{
		try
		{
			await _checkLock.WaitAsync(cancellationToken).ConfigureAwait(false);
		}
		catch(OperationCanceledException)
		{
			return new UpdateCheckResult(false, _currentVersion, null);
		}

		UpdateCheckResult result;
		try
		{
			GitHubReleaseModel? latest = await GetLatestRelease(cancellationToken);

			result = latest?.TagName is null || !HasRequiredAssets(latest)
				? new UpdateCheckResult(false, _currentVersion, null)
				: new UpdateCheckResult(
					IsNewerVersion(latest.TagName, _currentVersion),
					_currentVersion,
					latest);
		}
		catch
		{
			result = new UpdateCheckResult(false, _currentVersion, null);
		}
		finally
		{
			_checkLock.Release();
		}

		_cachedResult = result;
		_lastChecked = DateTime.UtcNow;
		OnUpdateChecked?.Invoke();
		return result;
	}

	async Task<GitHubReleaseModel?> GetLatestRelease(CancellationToken cancellationToken)
	{
		return await _httpClient.GetFromJsonAsync<GitHubReleaseModel>(latestReleaseUrl, releaseJsonOptions, cancellationToken);
	}

	internal static bool IsNewerVersion(string remoteVersion, string currentVersion)
	{
		string remote = remoteVersion.TrimStart('v');
		string current = currentVersion.TrimStart('v');

		try
		{
			string remoteCore = remote.Split(['-', '+'])[0];
			string currentCore = current.Split(['-', '+'])[0];

			bool remoteIsPreRelease = remote.Contains('-');
			bool currentIsPreRelease = current.Contains('-');

			List<int> remoteNums = [.. remoteCore.Split('.')
				.Select(p => int.TryParse(p, out int n) ? n : -1)
				.TakeWhile(n => n >= 0)];

			List<int> currentNums = [.. currentCore.Split('.')
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

			// Same numeric version: per SemVer, stable > pre-release.
			return !remoteIsPreRelease && currentIsPreRelease;
		}
		catch
		{
			return string.Compare(remote, current, StringComparison.OrdinalIgnoreCase) > 0;
		}
	}

	internal static bool HasRequiredAssets(GitHubReleaseModel release)
	{
		List<GitHubReleaseAssetModel>? assets = release.Assets;
		return assets is { Count: > 0 } &&
			assets.Any(a => a.Name?.EndsWith("-Setup.exe", StringComparison.OrdinalIgnoreCase) is true) &&
			assets.Any(a => a.Name?.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) is true);
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
		if(_disposed)
		{
			return;
		}

		_disposed = true;
		_cts.Cancel();
		_cts.Dispose();
		_checkLock.Dispose();
		_httpClient.Dispose();
		GC.SuppressFinalize(this);
	}
}