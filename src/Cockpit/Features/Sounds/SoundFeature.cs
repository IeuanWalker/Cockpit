using System.Collections.Concurrent;
using Cockpit.Features.AppSettings;
using Cockpit.Features.Permissions;
using Cockpit.Features.Permissions.Models;
using Cockpit.Features.Sessions;
using Cockpit.Features.Sessions.Models;
using Cockpit.Features.UserInputRequests;
using Microsoft.Extensions.Logging;
using Plugin.Maui.Audio;

namespace Cockpit.Features.Sounds;

public sealed partial class SoundFeature : IDisposable
{
	readonly IAudioManager _audioManager;
	readonly PermissionFeature _permissionFeature;
	readonly UserInputFeature _userInputFeature;
	readonly ISessionStateProvider _sessionStateProvider;
	readonly IAppSettingsFeature _appSettings;
	readonly ILogger<SoundFeature> _logger;

	readonly ConcurrentDictionary<string, SessionStatusEnum> _lastKnownStatuses = new();
	readonly Dictionary<string, byte[]> _soundBytes = [];

	// Default raw asset per sound name. Both "permission" and "userInput" fall back to request.mp3.
	static readonly Dictionary<string, string> defaultSoundAssets = new()
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
		IAudioManager audioManager,
		PermissionFeature permissionFeature,
		UserInputFeature userInputFeature,
		ISessionStateProvider sessionStateProvider,
		IAppSettingsFeature appSettings,
		ILogger<SoundFeature> logger)
	{
		_audioManager = audioManager;
		_permissionFeature = permissionFeature;
		_userInputFeature = userInputFeature;
		_sessionStateProvider = sessionStateProvider;
		_appSettings = appSettings;
		_logger = logger;

		_permissionFeature.OnPermissionRequested += OnPermissionRequested;
		_userInputFeature.OnUserInputRequested += OnUserInputRequested;
		_sessionStateProvider.OnStateChanged += OnSessionStateChanged;

		_ = LoadAllSoundsAsync();
	}

	// ── Loading ────────────────────────────────────────────────────────────────

	async Task LoadAllSoundsAsync()
	{
		await Task.WhenAll(defaultSoundAssets.Keys.Select(LoadSingleSoundAsync));
	}

	async Task LoadSingleSoundAsync(string soundName)
	{
		try
		{
			string customPath = CustomSoundPath(soundName);

			if(File.Exists(customPath))
			{
				_soundBytes[soundName] = await File.ReadAllBytesAsync(customPath);
			}
			else
			{
				using Stream stream = await FileSystem.OpenAppPackageFileAsync(defaultSoundAssets[soundName]);
				using MemoryStream ms = new();
				await stream.CopyToAsync(ms);
				_soundBytes[soundName] = ms.ToArray();
			}
		}
		catch(Exception ex)
		{
			_logger.LogWarning(ex, "Failed to load sound {SoundName}", soundName);
		}
	}

	// ── Custom sound management ────────────────────────────────────────────────

	/// <summary>
	/// Saves <paramref name="stream"/> as the custom sound for <paramref name="soundType"/>.
	/// </summary>
	public async Task SetCustomSoundAsync(SoundEffectType soundType, Stream stream, string displayFileName)
	{
		string soundName = GetSoundName(soundType);
		string customDir = Path.Combine(FileSystem.AppDataDirectory, "Sounds");
		Directory.CreateDirectory(customDir);

		using(FileStream fs = File.Create(CustomSoundPath(soundName)))
		{
			await stream.CopyToAsync(fs);
		}

		StoreCustomFileName(soundType, displayFileName);
		await LoadSingleSoundAsync(soundName);
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

			// Sessions finish by transitioning to Idle after Running (not a Finished enum value).
			bool wasActive = lastStatus == SessionStatusEnum.Running
				|| lastStatus == SessionStatusEnum.NeedsPermission
				|| lastStatus == SessionStatusEnum.NeedsUserInput;

			if(session.Status == SessionStatusEnum.Idle && wasActive)
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

		if(!_soundBytes.TryGetValue(soundName, out byte[]? bytes))
		{
			return;
		}

		double volume = soundType switch
		{
			SoundEffectType.Permission => _appSettings.SoundPermissionVolume,
			SoundEffectType.UserInput => _appSettings.SoundUserInputVolume,
			SoundEffectType.Finished => _appSettings.SoundFinishedVolume,
			_ => 0.5
		};

		try
		{
			IAudioPlayer player = _audioManager.CreatePlayer(new MemoryStream(bytes));
			player.Volume = volume;
			player.PlaybackEnded += OnEnded;
			await Task.Run(player.Play);

			void OnEnded(object? s, EventArgs e)
			{
				player.PlaybackEnded -= OnEnded;
				player.Dispose();
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
