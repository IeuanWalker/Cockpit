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
	static readonly HashSet<string> allowedDownloadHosts = new(StringComparer.OrdinalIgnoreCase)
	{
		"github.com",
		"release-assets.githubusercontent.com"
	};
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
	readonly HttpClient _downloadHttpClient;
	readonly bool _ownsDownloadClient;
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
	int _disposed;

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
		_downloadHttpClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
		_downloadHttpClient.DefaultRequestHeaders.Add("User-Agent", "Cockpit");
		_ownsDownloadClient = true;
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
		ISessionStateProvider? sessionStateProvider = null,
		HttpClient? downloadHttpClient = null)
	{
		_httpClient = httpClient;
		_downloadHttpClient = downloadHttpClient ?? _httpClient;
		_ownsDownloadClient = downloadHttpClient is not null && !ReferenceEquals(downloadHttpClient, httpClient);
		_logger = logger ?? NullLogger<UpdateFeature>.Instance;
		_currentVersion = currentVersion;
		_userSettings = userSettings;
		_sessionStateProvider = sessionStateProvider;
		_downloadRootDirectory = downloadRootDirectory ?? Path.Combine(Path.GetTempPath(), "Cockpit-UpdateTests");
		IsInstalledBuild = isInstalledBuild;
		InstalledDate = null;

		_sessionStateProvider?.OnStateChanged += HandleSessionStateChanged;
	}

	/// <summary>
	/// Starts the periodic update check. Call once after the application has started.
	/// Subsequent calls are no-ops.
	/// </summary>
	public void Initialize()
	{
		_checkLock.Wait();
		try
		{
			if(_checkTask is not null)
			{
				return;
			}

			_checkTask = RunPeriodicCheckAsync(_cts.Token);
		}
		finally
		{
			_checkLock.Release();
		}
	}

	async Task RunPeriodicCheckAsync(CancellationToken cancellationToken)
	{
		using PeriodicTimer timer = new(checkInterval);
		do
		{
			try
			{
				await CheckForUpdate(cancellationToken).ConfigureAwait(false);
			}
			catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested)
			{
				return;
			}
			catch(Exception ex)
			{
				_logger.LogWarning(ex, "Periodic update check failed, will retry next interval.");
			}
		}
		while(await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false));
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
		bool shouldEvaluateAutoInstall = false;
		try
		{
			await _downloadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
		}
		catch(OperationCanceledException)
		{
			return;
		}

		try
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

			GitHubReleaseAssetModel? installerAsset = FindInstallerAsset(release);
			if(installerAsset is null || string.IsNullOrWhiteSpace(installerAsset.BrowserDownloadUrl) || installerAsset.Size <= 0)
			{
				SetDownloadFailed("Installer asset not found in latest release.");
				return;
			}

			if(_downloadState.Status is UpdateDownloadStatusEnum.Downloading
				or UpdateDownloadStatusEnum.Downloaded
				or UpdateDownloadStatusEnum.Installing)
			{
				return;
			}

			string versionTag = release.TagName!;
			string safeVersionTag = SanitizePathSegment(versionTag);
			string fileName = GetExpectedInstallerFileName(versionTag);
			if(!IsExpectedInstallerDownload(installerAsset, versionTag))
			{
				SetDownloadFailed("Installer asset URL is not an expected Cockpit GitHub release download.");
				return;
			}

			string targetDirectory = Path.Combine(_downloadRootDirectory, safeVersionTag);
			Directory.CreateDirectory(targetDirectory);
			string installerPath = Path.Combine(targetDirectory, fileName);
			string partialInstallerPath = installerPath + ".part";
			DeleteFileIfExists(partialInstallerPath);

			using HttpRequestMessage request = new(HttpMethod.Get, installerAsset.BrowserDownloadUrl);
			using HttpResponseMessage response = await _downloadHttpClient
				.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
				.ConfigureAwait(false);
			response.EnsureSuccessStatusCode();
			if(!IsAllowedDownloadUri(response.RequestMessage?.RequestUri))
			{
				throw new InvalidDataException("The installer download redirected to an unexpected host.");
			}

			long? totalBytes = response.Content.Headers.ContentLength;
			if(totalBytes.HasValue && totalBytes.Value != installerAsset.Size)
			{
				throw new InvalidDataException("The installer content length does not match the GitHub release asset size.");
			}
			_downloadState = new UpdateDownloadStateModel(
				UpdateDownloadStatusEnum.Downloading,
				versionTag,
				installerPath,
				0,
				totalBytes,
				null);
			OnUpdateChecked?.Invoke();

			byte[] buffer = new byte[81920];
			long bytesDownloaded = 0;
			long lastNotifyTicks = Environment.TickCount64;
			await using(Stream source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
			await using(FileStream target = new(partialInstallerPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
			{
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
			}

			if(bytesDownloaded != installerAsset.Size)
			{
				throw new InvalidDataException("The downloaded installer size does not match the GitHub release asset size.");
			}

			File.Move(partialInstallerPath, installerPath, true);
			_downloadState = new UpdateDownloadStateModel(
				UpdateDownloadStatusEnum.Downloaded,
				versionTag,
				installerPath,
				bytesDownloaded,
				totalBytes ?? bytesDownloaded,
				null);
			CleanupStaleInstallerDownloads(targetDirectory);
			_autoInstallPending = false;
			OnUpdateChecked?.Invoke();
			shouldEvaluateAutoInstall = true;
		}
		catch(OperationCanceledException)
		{
			string? installerPath = _downloadState.InstallerPath;
			if(!string.IsNullOrWhiteSpace(installerPath))
			{
				try
				{
					DeleteFileIfExists(installerPath);
					DeleteFileIfExists(installerPath + ".part");
				}
				catch(Exception deleteEx)
				{
					_logger.LogWarning(deleteEx, "Failed to delete partial installer {InstallerPath}", installerPath);
				}
			}

			_autoInstallPending = false;
			_downloadState = UpdateDownloadStateModel.Idle;
			OnUpdateChecked?.Invoke();
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Failed to download update installer.");

			string? installerPath = _downloadState.InstallerPath;
			if(!string.IsNullOrWhiteSpace(installerPath))
			{
				try
				{
					DeleteFileIfExists(installerPath);
					DeleteFileIfExists(installerPath + ".part");
				}
				catch(Exception deleteEx)
				{
					_logger.LogWarning(deleteEx, "Failed to delete partial installer {InstallerPath}", installerPath);
				}
			}

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

			string installerPath = _downloadState.InstallerPath;
			_autoInstallPending = false;
			_downloadState = _downloadState with { Status = UpdateDownloadStatusEnum.Installing, ErrorMessage = null };
			OnUpdateChecked?.Invoke();

			bool launched = LaunchInstaller(installerPath);
			if(!launched)
			{
				SetDownloadFailed("Permission was denied or the installer could not be launched.");
				return;
			}

			Application.Current?.Dispatcher.Dispatch(() => Application.Current.Quit());
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
		return FindInstallerAsset(release) is not null;
	}

	internal static GitHubReleaseAssetModel? FindInstallerAsset(GitHubReleaseModel release)
	{
		if(string.IsNullOrWhiteSpace(release.TagName))
		{
			return null;
		}

		return release.Assets?.FirstOrDefault(a => IsExpectedInstallerDownload(a, release.TagName));
	}

	internal static string GetExpectedInstallerFileName(string versionTag) =>
		$"Cockpit-windows-x64-{versionTag.TrimStart('v', 'V')}-Setup.exe";

	internal static bool IsExpectedInstallerDownload(GitHubReleaseAssetModel asset, string versionTag)
	{
		string expectedFileName = GetExpectedInstallerFileName(versionTag);
		if(asset.Size <= 0 || !string.Equals(asset.Name, expectedFileName, StringComparison.Ordinal))
		{
			return false;
		}

		if(!Uri.TryCreate(asset.BrowserDownloadUrl, UriKind.Absolute, out Uri? uri) ||
			!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
			!string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
			!string.IsNullOrEmpty(uri.Query) ||
			!string.IsNullOrEmpty(uri.Fragment))
		{
			return false;
		}

		string expectedPath = $"/IeuanWalker/Cockpit/releases/download/{versionTag}/{expectedFileName}";
		return string.Equals(Uri.UnescapeDataString(uri.AbsolutePath), expectedPath, StringComparison.Ordinal);
	}

	internal static bool IsAllowedDownloadUri(Uri? uri) =>
		uri is not null &&
		string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
		allowedDownloadHosts.Contains(uri.Host);

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

	internal static bool IsSessionActive(AgentRunStateEnum runState)
	{
		return runState is AgentRunStateEnum.Running;
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
		if(Interlocked.Exchange(ref _disposed, 1) != 0)
		{
			return;
		}

		_cts.Cancel();
		_sessionStateProvider?.OnStateChanged -= HandleSessionStateChanged;

		_cts.Dispose();
		_checkLock.Dispose();
		_downloadLock.Dispose();
		if(_ownsDownloadClient)
		{
			_downloadHttpClient.Dispose();
		}
		_httpClient.Dispose();
		GC.SuppressFinalize(this);
	}

	void HandleSessionStateChanged()
	{
		if(!_autoInstallPending)
		{
			return;
		}

		_ = EvaluateAutoInstallAfterDownloadAsync(_cts.Token)
			.ContinueWith(
				t => _logger.LogError(t.Exception, "Auto-install evaluation failed"),
				TaskContinuationOptions.OnlyOnFaulted);
	}

	bool HasActiveSessions()
	{
		if(_sessionStateProvider is null)
		{
			return false;
		}

		return _sessionStateProvider.Sessions.Any(s => IsSessionActive(s.Lifecycle.AgentRunState));
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

	static void DeleteFileIfExists(string path)
	{
		if(File.Exists(path))
		{
			File.Delete(path);
		}
	}

	bool LaunchInstaller(string installerPath)
	{
		if(!OperatingSystem.IsWindows())
		{
			return false;
		}

		try
		{
			ProcessStartInfo startInfo = new()
			{
				FileName = installerPath,
				UseShellExecute = true
			};

			string? workingDirectory = Path.GetDirectoryName(installerPath);
			if(!string.IsNullOrWhiteSpace(workingDirectory))
			{
				startInfo.WorkingDirectory = workingDirectory;
			}

			using Process? installerProcess = Process.Start(startInfo);
			return installerProcess is not null;
		}
		catch(Exception ex)
		{
			_logger.LogWarning(ex, "Failed to launch NSIS installer.");
			return false;
		}
	}

	void CleanupStaleInstallerDownloads(string currentTargetDirectory)
	{
		string? rootDirectory = Path.GetDirectoryName(currentTargetDirectory);
		if(string.IsNullOrWhiteSpace(rootDirectory) || !Directory.Exists(rootDirectory))
		{
			return;
		}

		string currentTargetFullPath = Path.GetFullPath(currentTargetDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		foreach(string directory in Directory.EnumerateDirectories(rootDirectory))
		{
			string candidateDirectory = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			if(string.Equals(candidateDirectory, currentTargetFullPath, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			if(!Directory.EnumerateFiles(candidateDirectory, "*-Setup.exe", SearchOption.TopDirectoryOnly).Any())
			{
				continue;
			}

			try
			{
				Directory.Delete(candidateDirectory, true);
			}
			catch(Exception ex)
			{
				_logger.LogWarning(ex, "Failed to delete stale installer directory {Directory}", candidateDirectory);
			}
		}
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
