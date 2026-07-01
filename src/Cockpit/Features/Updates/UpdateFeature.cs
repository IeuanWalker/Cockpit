using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Cockpit.Features.Sessions;
using Cockpit.Features.Sessions.Models;
using Cockpit.Features.Updates.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
#if WINDOWS
using Microsoft.Win32;
#endif

namespace Cockpit.Features.Updates;

public sealed partial class UpdateFeature : IDisposable
{
	static readonly TimeSpan checkInterval = TimeSpan.FromHours(1);
	static readonly string latestReleaseUrl = "https://api.github.com/repos/IeuanWalker/Cockpit/releases/latest";
#if WINDOWS
	static readonly string appInstallRegistryPath = @"Software\Cockpit";
	static readonly string appInstallRegistryValue = "Install_Dir";
	static readonly string appUninstallRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\com.ieuanwalker.cockpit";
	static readonly string appInstallLocationValue = "InstallLocation";
	static readonly string appUninstallStringValue = "UninstallString";
#endif

	static readonly JsonSerializerOptions releaseJsonOptions = new()
	{
		PropertyNameCaseInsensitive = true,
		PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
	};

	readonly HttpClient _httpClient;
	readonly ILogger<UpdateFeature> _logger;
	readonly string _currentVersion;
	readonly UserAppSettings? _userSettings;
	readonly ISessionStateProvider? _sessionStateProvider;
	readonly string _downloadRootDirectory;
	readonly CancellationTokenSource _cts = new();
	readonly SemaphoreSlim _checkLock = new(1, 1);
	readonly SemaphoreSlim _downloadLock = new(1, 1);
	Task? _checkTask;

	UpdateCheckResult? _cachedResult;
	UpdateDownloadStateModel _downloadState = UpdateDownloadStateModel.Idle;
	string? _dismissedVersion;
	DateTime? _lastChecked;
	bool _autoInstallPending;
	bool _disposed;

	public UpdateCheckResult? CachedResult => _cachedResult;
	public string? DismissedVersion => _dismissedVersion;
	public string CurrentVersion => _currentVersion;
	public UpdateDownloadStateModel DownloadState => _downloadState;
	public bool AutoInstallPending => _autoInstallPending;

	public DateTime? LastChecked => _lastChecked;
	public DateTime? InstalledDate { get; }
	public bool IsInstalledBuild { get; }
	public bool IsPortableBuild => !IsInstalledBuild;

	public bool AutoInstallAfterDownloadIfNoActiveSession
	{
		get => _userSettings?.AutoInstallDownloadedUpdateWhenNoActiveSession ?? false;
		set
		{
			if(_userSettings is null)
			{
				return;
			}

			_userSettings.AutoInstallDownloadedUpdateWhenNoActiveSession = value;
			OnUpdateChecked?.Invoke();
		}
	}

	public event Action? OnUpdateChecked;

	public UpdateFeature(
		HttpClient httpClient,
		ILogger<UpdateFeature> logger,
		UserAppSettings userSettings,
		ISessionStateProvider sessionStateProvider)
	{
		_httpClient = httpClient;
		_logger = logger;
		_userSettings = userSettings;
		_sessionStateProvider = sessionStateProvider;
		_currentVersion = AppInfo.VersionString;
		_downloadRootDirectory = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
			"Cockpit",
			"Updates");
		IsInstalledBuild = IsInstalledPath(Environment.ProcessPath, TryGetInstalledDirectoryFromRegistry(logger));
		_sessionStateProvider.OnStateChanged += HandleSessionStateChanged;

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
	internal UpdateFeature(
		HttpClient httpClient,
		string currentVersion,
		ILogger<UpdateFeature>? logger = null,
		bool isInstalledBuild = false,
		string? downloadRootDirectory = null,
		UserAppSettings? userSettings = null,
		ISessionStateProvider? sessionStateProvider = null)
	{
		_httpClient = httpClient;
		_logger = logger ?? NullLogger<UpdateFeature>.Instance;
		_currentVersion = currentVersion;
		_userSettings = userSettings;
		_sessionStateProvider = sessionStateProvider;
		_downloadRootDirectory = downloadRootDirectory ?? Path.Combine(Path.GetTempPath(), "Cockpit-UpdateTests");
		IsInstalledBuild = isInstalledBuild;
		InstalledDate = null;

		if(_sessionStateProvider is not null)
		{
			_sessionStateProvider.OnStateChanged += HandleSessionStateChanged;
		}
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
		catch(Exception ex)
		{
			_logger.LogWarning(ex, "Periodic update check failed, will retry");
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

	public async Task DownloadLatestInstallerAsync(CancellationToken cancellationToken = default)
	{
		if(!IsInstalledBuild)
		{
			SetDownloadFailed("In-app download is only available for installed builds.");
			return;
		}

		GitHubReleaseModel? release = _cachedResult?.LatestRelease;
		if(release is null)
		{
			SetDownloadFailed("No update release metadata available.");
			return;
		}

		GitHubReleaseAssetModel? setupAsset = FindSetupAsset(release);
		if(setupAsset is null || string.IsNullOrWhiteSpace(setupAsset.BrowserDownloadUrl))
		{
			SetDownloadFailed("Installer asset not found in latest release.");
			return;
		}

		bool shouldEvaluateAutoInstall = false;
		try
		{
			await _downloadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
		}
		catch(OperationCanceledException)
		{
			SetDownloadFailed("Download cancelled.");
			return;
		}

		try
		{
			string versionTag = string.IsNullOrWhiteSpace(release.TagName) ? "latest" : release.TagName;
			string safeVersionTag = SanitizePathSegment(versionTag);
			string fileName = string.IsNullOrWhiteSpace(setupAsset.Name)
				? $"Cockpit-{safeVersionTag}-Setup.exe"
				: setupAsset.Name;
			string targetDirectory = Path.Combine(_downloadRootDirectory, safeVersionTag);
			Directory.CreateDirectory(targetDirectory);
			string installerPath = Path.Combine(targetDirectory, fileName);

			using HttpRequestMessage request = new(HttpMethod.Get, setupAsset.BrowserDownloadUrl);
			using HttpResponseMessage response = await _httpClient
				.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
				.ConfigureAwait(false);
			response.EnsureSuccessStatusCode();

			long? totalBytes = response.Content.Headers.ContentLength;
			_downloadState = new UpdateDownloadStateModel(
				UpdateDownloadStatusEnum.Downloading,
				versionTag,
				installerPath,
				0,
				totalBytes,
				null);
			OnUpdateChecked?.Invoke();

			await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
			await using FileStream target = new(installerPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

			byte[] buffer = new byte[81920];
			long bytesDownloaded = 0;
			long lastNotifyTicks = Environment.TickCount64;
			while(true)
			{
				int bytesRead = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
				if(bytesRead == 0)
				{
					break;
				}

				await target.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
				bytesDownloaded += bytesRead;

				long currentTicks = Environment.TickCount64;
				if(currentTicks - lastNotifyTicks >= 120)
				{
					_downloadState = new UpdateDownloadStateModel(
						UpdateDownloadStatusEnum.Downloading,
						versionTag,
						installerPath,
						bytesDownloaded,
						totalBytes,
						null);
					OnUpdateChecked?.Invoke();
					lastNotifyTicks = currentTicks;
				}
			}

			await target.FlushAsync(cancellationToken).ConfigureAwait(false);
			_downloadState = new UpdateDownloadStateModel(
				UpdateDownloadStatusEnum.Downloaded,
				versionTag,
				installerPath,
				bytesDownloaded,
				totalBytes ?? bytesDownloaded,
				null);
			_autoInstallPending = false;
			OnUpdateChecked?.Invoke();
			shouldEvaluateAutoInstall = true;
		}
		catch(OperationCanceledException)
		{
			SetDownloadFailed("Download cancelled.");
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Failed to download update installer.");
			SetDownloadFailed("Failed to download update installer.");
		}
		finally
		{
			_downloadLock.Release();
		}

		if(shouldEvaluateAutoInstall)
		{
			await EvaluateAutoInstallAfterDownloadAsync(cancellationToken).ConfigureAwait(false);
		}
	}

	public async Task InstallDownloadedUpdateAsync(CancellationToken cancellationToken = default)
	{
		if(!OperatingSystem.IsWindows())
		{
			SetDownloadFailed("Install is only supported on Windows.");
			return;
		}

		try
		{
			await _downloadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
		}
		catch(OperationCanceledException)
		{
			SetDownloadFailed("Install cancelled.");
			return;
		}

		try
		{
			if(_downloadState.Status is not UpdateDownloadStatusEnum.Downloaded || string.IsNullOrWhiteSpace(_downloadState.InstallerPath))
			{
				SetDownloadFailed("No downloaded installer available.");
				return;
			}

			if(!File.Exists(_downloadState.InstallerPath))
			{
				SetDownloadFailed("Downloaded installer was not found on disk.");
				return;
			}

			_autoInstallPending = false;
			_downloadState = _downloadState with { Status = UpdateDownloadStatusEnum.Installing, ErrorMessage = null };
			OnUpdateChecked?.Invoke();

			ProcessStartInfo installerStartInfo = new()
			{
				FileName = _downloadState.InstallerPath,
				UseShellExecute = true,
				Verb = "runas",
				WorkingDirectory = Path.GetDirectoryName(_downloadState.InstallerPath)
			};
			Process? started = Process.Start(installerStartInfo);
			if(started is null)
			{
				SetDownloadFailed("Failed to launch installer.");
				return;
			}

			Application.Current?.Quit();
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Failed to start installer.");
			SetDownloadFailed("Failed to start installer.");
		}
		finally
		{
			_downloadLock.Release();
		}
	}

	public async Task EvaluateAutoInstallAfterDownloadAsync(CancellationToken cancellationToken = default)
	{
		if(_downloadState.Status is not UpdateDownloadStatusEnum.Downloaded)
		{
			_autoInstallPending = false;
			OnUpdateChecked?.Invoke();
			return;
		}

		if(!AutoInstallAfterDownloadIfNoActiveSession)
		{
			_autoInstallPending = false;
			OnUpdateChecked?.Invoke();
			return;
		}

		if(HasActiveSessions())
		{
			_autoInstallPending = true;
			OnUpdateChecked?.Invoke();
			return;
		}

		_autoInstallPending = false;
		OnUpdateChecked?.Invoke();
		await InstallDownloadedUpdateAsync(cancellationToken).ConfigureAwait(false);
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

	internal static GitHubReleaseAssetModel? FindSetupAsset(GitHubReleaseModel release)
	{
		return release.Assets?.FirstOrDefault(a => a.Name?.EndsWith("-Setup.exe", StringComparison.OrdinalIgnoreCase) is true);
	}

	internal static bool IsInstalledPath(string? executablePath, string? installDirectory)
	{
		if(string.IsNullOrWhiteSpace(executablePath) || string.IsNullOrWhiteSpace(installDirectory))
		{
			return false;
		}

		try
		{
			string executableDirectory = Path.GetDirectoryName(Path.GetFullPath(executablePath)) ?? string.Empty;
			string normalizedExeDirectory = executableDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			string normalizedInstallDirectory = Path.GetFullPath(installDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

			if(string.Equals(normalizedExeDirectory, normalizedInstallDirectory, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}

			string installPrefix = normalizedInstallDirectory + Path.DirectorySeparatorChar;
			return normalizedExeDirectory.StartsWith(installPrefix, StringComparison.OrdinalIgnoreCase);
		}
		catch
		{
			return false;
		}
	}

	internal static bool IsSessionActive(SessionStatusEnum status)
	{
		return status is SessionStatusEnum.Running
			or SessionStatusEnum.NeedsPermission
			or SessionStatusEnum.NeedsUserInput
			or SessionStatusEnum.NeedsElicitation;
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
		if(_sessionStateProvider is not null)
		{
			_sessionStateProvider.OnStateChanged -= HandleSessionStateChanged;
		}

		_cts.Dispose();
		_checkLock.Dispose();
		_downloadLock.Dispose();
		_httpClient.Dispose();
		GC.SuppressFinalize(this);
	}

	void HandleSessionStateChanged()
	{
		if(!_autoInstallPending)
		{
			return;
		}

		_ = EvaluateAutoInstallAfterDownloadAsync();
	}

	bool HasActiveSessions()
	{
		if(_sessionStateProvider is null)
		{
			return false;
		}

		return _sessionStateProvider.Sessions.Any(s => IsSessionActive(s.Status));
	}

	void SetDownloadFailed(string errorMessage)
	{
		_autoInstallPending = false;
		_downloadState = _downloadState with
		{
			Status = UpdateDownloadStatusEnum.Failed,
			ErrorMessage = errorMessage
		};
		OnUpdateChecked?.Invoke();
	}

	static string SanitizePathSegment(string value)
	{
		char[] invalid = Path.GetInvalidFileNameChars();
		char[] chars = value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
		return new string(chars);
	}

	static string? TryGetInstalledDirectoryFromRegistry(ILogger logger)
	{
#if WINDOWS
		try
		{
			foreach(RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
			{
				using RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);

				using RegistryKey? appKey = baseKey.OpenSubKey(appInstallRegistryPath);
				string? directInstallPath = appKey?.GetValue(appInstallRegistryValue) as string;
				if(!string.IsNullOrWhiteSpace(directInstallPath))
				{
					return directInstallPath;
				}

				using RegistryKey? uninstallKey = baseKey.OpenSubKey(appUninstallRegistryPath);
				string? uninstallInstallPath = uninstallKey?.GetValue(appInstallLocationValue) as string;
				if(!string.IsNullOrWhiteSpace(uninstallInstallPath))
				{
					return uninstallInstallPath;
				}

				string? uninstallString = uninstallKey?.GetValue(appUninstallStringValue) as string;
				string? derivedInstallPath = TryGetInstallPathFromUninstallString(uninstallString);
				if(!string.IsNullOrWhiteSpace(derivedInstallPath))
				{
					return derivedInstallPath;
				}
			}

			return null;
		}
		catch(Exception ex)
		{
			logger.LogWarning(ex, "Failed to inspect install registry metadata.");
			return null;
		}
#else
		_ = logger;
		return null;
#endif
	}

	static string? TryGetInstallPathFromUninstallString(string? uninstallString)
	{
		if(string.IsNullOrWhiteSpace(uninstallString))
		{
			return null;
		}

		string trimmed = uninstallString.Trim();
		string uninstallPath = trimmed;
		if(trimmed.StartsWith('"'))
		{
			int endQuote = trimmed.IndexOf('"', 1);
			if(endQuote > 1)
			{
				uninstallPath = trimmed[1..endQuote];
			}
		}
		else
		{
			int firstSpace = trimmed.IndexOf(' ');
			if(firstSpace > 0)
			{
				uninstallPath = trimmed[..firstSpace];
			}
		}

		try
		{
			string fullPath = Path.GetFullPath(uninstallPath);
			string? installDirectory = Path.GetDirectoryName(fullPath);
			return string.IsNullOrWhiteSpace(installDirectory) ? null : installDirectory;
		}
		catch
		{
			return null;
		}
	}
}