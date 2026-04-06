using System.Collections.Concurrent;
using System.Globalization;
using Cockpit.Features.AppSettings;
using Cockpit.Features.Permissions;
using Cockpit.Features.Permissions.Models;
using Cockpit.Features.Sessions;
using Cockpit.Features.Sessions.Models;
using Cockpit.Features.Splash;
using Cockpit.Features.UserInputRequests;
using Microsoft.Extensions.Logging;

namespace Cockpit.Features.Sounds;

public sealed class SoundFeature : IDisposable
{
	readonly PermissionFeature _permissionFeature;
	readonly UserInputFeature _userInputFeature;
	readonly ISessionStateProvider _sessionStateProvider;
	readonly IAppSettingsFeature _appSettings;
	readonly ILogger<SoundFeature> _logger;

	readonly ConcurrentDictionary<string, SessionStatusEnum> _lastKnownStatuses = new();
	readonly Dictionary<string, string> _soundDataUrls = [];
	bool _soundsRegistered = false;

	// Default raw asset per sound name. Both "permission" and "userInput" fall back to request.mp3.
	static readonly Dictionary<string, string> DefaultSoundAssets = new()
	{
		["permission"] = "Sounds/request.mp3",
		["userInput"] = "Sounds/request.mp3",
		["finished"] = "Sounds/finished.mp3"
	};

	static string CustomSoundPath(string soundName) =>
		Path.Combine(FileSystem.AppDataDirectory, "Sounds", $"{soundName}.mp3");

	static string GetSoundName(SoundEffectType soundType) => soundType switch
	{
		SoundEffectType.Permission => "permission",
		SoundEffectType.UserInput => "userInput",
		SoundEffectType.Finished => "finished",
		_ => "finished"
	};

	public SoundFeature(
		PermissionFeature permissionFeature,
		UserInputFeature userInputFeature,
		ISessionStateProvider sessionStateProvider,
		IAppSettingsFeature appSettings,
		SplashFeature splashFeature,
		ILogger<SoundFeature> logger)
	{
		_permissionFeature = permissionFeature;
		_userInputFeature = userInputFeature;
		_sessionStateProvider = sessionStateProvider;
		_appSettings = appSettings;
		_logger = logger;

		_permissionFeature.OnPermissionRequested += OnPermissionRequested;
		_userInputFeature.OnUserInputRequested += OnUserInputRequested;
		_sessionStateProvider.OnStateChanged += OnSessionStateChanged;

		_ = LoadAllSoundsAsync();
		splashFeature.OnBlazorReady += OnBlazorReady;
	}

	// ── Loading ────────────────────────────────────────────────────────────────

	async Task LoadAllSoundsAsync()
	{
		await Task.WhenAll(DefaultSoundAssets.Keys.Select(LoadSingleSoundAsync));
	}

	async Task LoadSingleSoundAsync(string soundName)
	{
		try
		{
			string customPath = CustomSoundPath(soundName);
			byte[] bytes;

			if(File.Exists(customPath))
			{
				bytes = await File.ReadAllBytesAsync(customPath);
			}
			else
			{
				using Stream stream = await FileSystem.OpenAppPackageFileAsync(DefaultSoundAssets[soundName]);
				using MemoryStream ms = new();
				await stream.CopyToAsync(ms);
				bytes = ms.ToArray();
			}

			_soundDataUrls[soundName] = $"data:audio/mpeg;base64,{Convert.ToBase64String(bytes)}";
		}
		catch(Exception ex)
		{
			_logger.LogWarning(ex, "Failed to load sound {SoundName}", soundName);
		}
	}

	// ── JS Registration ────────────────────────────────────────────────────────

	void OnBlazorReady() => _ = RegisterAllSoundsWithJsAsync();

	async Task RegisterAllSoundsWithJsAsync()
	{
		if(_soundsRegistered)
		{
			return;
		}

		// Wait for async file loading to complete
		while(_soundDataUrls.Count < DefaultSoundAssets.Count)
		{
			await Task.Delay(50);
		}

		foreach(string name in _soundDataUrls.Keys)
		{
			await RegisterSingleSoundWithJsAsync(name);
		}

		_soundsRegistered = true;
	}

	async Task RegisterSingleSoundWithJsAsync(string soundName)
	{
		if(!_soundDataUrls.TryGetValue(soundName, out string? dataUrl))
		{
			return;
		}

		if(Application.Current?.Windows?.FirstOrDefault()?.Page is MainPage mainPage)
		{
			await mainPage.InvokeJavaScriptAsync($"window.cockpit?.registerSound?.('{soundName}', '{dataUrl}');");
		}
	}

	// ── Custom sound management ────────────────────────────────────────────────

	/// <summary>
	/// Saves <paramref name="stream"/> as the custom sound for <paramref name="soundType"/>,
	/// then reloads and re-registers it with the JS audio cache.
	/// </summary>
	public async Task SetCustomSoundAsync(SoundEffectType soundType, Stream stream, string displayFileName)
	{
		string soundName = GetSoundName(soundType);
		string customDir = Path.Combine(FileSystem.AppDataDirectory, "Sounds");
		Directory.CreateDirectory(customDir);

		string customPath = CustomSoundPath(soundName);
		using(FileStream fs = File.Create(customPath))
		{
			await stream.CopyToAsync(fs);
		}

		StoreCustomFileName(soundType, displayFileName);

		await LoadSingleSoundAsync(soundName);
		await RegisterSingleSoundWithJsAsync(soundName);
	}

	/// <summary>
	/// Removes any custom sound for <paramref name="soundType"/> and reverts to the bundled default.
	/// </summary>
	public async Task ResetToDefaultAsync(SoundEffectType soundType)
	{
		string soundName = GetSoundName(soundType);
		string customPath = CustomSoundPath(soundName);

		if(File.Exists(customPath))
		{
			File.Delete(customPath);
		}

		StoreCustomFileName(soundType, string.Empty);

		await LoadSingleSoundAsync(soundName);
		await RegisterSingleSoundWithJsAsync(soundName);
	}

	/// <summary>Returns the user-supplied file name, or an empty string when using the default.</summary>
	public string GetCustomFileName(SoundEffectType soundType) => soundType switch
	{
		SoundEffectType.Permission => _appSettings.SoundPermissionCustomFileName,
		SoundEffectType.UserInput => _appSettings.SoundUserInputCustomFileName,
		SoundEffectType.Finished => _appSettings.SoundFinishedCustomFileName,
		_ => string.Empty
	};

	void StoreCustomFileName(SoundEffectType soundType, string fileName)
	{
		switch(soundType)
		{
			case SoundEffectType.Permission: _appSettings.SoundPermissionCustomFileName = fileName; break;
			case SoundEffectType.UserInput: _appSettings.SoundUserInputCustomFileName = fileName; break;
			case SoundEffectType.Finished: _appSettings.SoundFinishedCustomFileName = fileName; break;
		}
	}

	// ── Playback ───────────────────────────────────────────────────────────────

	void OnPermissionRequested(string sessionId, PermissionRequestModel request) =>
		_ = PlaySoundAsync(SoundEffectType.Permission);

	void OnUserInputRequested(string sessionId, UserInputRequestModel request) =>
		_ = PlaySoundAsync(SoundEffectType.UserInput);

	void OnSessionStateChanged()
	{
		foreach(SessionModel session in _sessionStateProvider.Sessions)
		{
			_lastKnownStatuses.TryGetValue(session.Id, out SessionStatusEnum lastStatus);

			if(session.Status == SessionStatusEnum.Finished && lastStatus != SessionStatusEnum.Finished)
			{
				_ = PlaySoundAsync(SoundEffectType.Finished);
			}

			_lastKnownStatuses[session.Id] = session.Status;
		}

		foreach(string id in _lastKnownStatuses.Keys)
		{
			if(!_sessionStateProvider.Sessions.Any(s => s.Id == id))
			{
				_lastKnownStatuses.TryRemove(id, out _);
			}
		}
	}

	/// <summary>
	/// Plays a sound. Pass <paramref name="forPreview"/> = <c>true</c> from the settings
	/// page to bypass the per-sound enabled toggle.
	/// </summary>
	public async Task PlaySoundAsync(SoundEffectType soundType, bool forPreview = false)
	{
		bool enabled = soundType switch
		{
			SoundEffectType.Permission => _appSettings.SoundPermissionEnabled,
			SoundEffectType.UserInput => _appSettings.SoundUserInputEnabled,
			SoundEffectType.Finished => _appSettings.SoundFinishedEnabled,
			_ => false
		};

		if(!forPreview && !enabled)
		{
			return;
		}

		string soundName = GetSoundName(soundType);
		float volume = soundType switch
		{
			SoundEffectType.Permission => _appSettings.SoundPermissionVolume,
			SoundEffectType.UserInput => _appSettings.SoundUserInputVolume,
			SoundEffectType.Finished => _appSettings.SoundFinishedVolume,
			_ => 0.5f
		};

		string volumeStr = volume.ToString("0.00", CultureInfo.InvariantCulture);

		try
		{
			if(Application.Current?.Windows?.FirstOrDefault()?.Page is MainPage mainPage)
			{
				await mainPage.InvokeJavaScriptAsync($"window.cockpit?.playSound?.('{soundName}', {volumeStr});");
			}
		}
		catch(Exception ex)
		{
			_logger.LogDebug(ex, "Failed to play sound effect {SoundType}", soundType);
		}
	}

	public void Dispose()
	{
		_permissionFeature.OnPermissionRequested -= OnPermissionRequested;
		_userInputFeature.OnUserInputRequested -= OnUserInputRequested;
		_sessionStateProvider.OnStateChanged -= OnSessionStateChanged;
	}
}
