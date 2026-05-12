using System.Collections.Concurrent;
using Cockpit.Features.AppSettings;
using Cockpit.Features.Permissions;
using Cockpit.Features.Permissions.Models;
using Cockpit.Features.SessionEvents.Handlers;
using Cockpit.Features.UserInputRequests;
using Microsoft.Extensions.Logging;
using Plugin.Maui.Audio;

namespace Cockpit.Features.Sounds;

public sealed class SoundFeature
{
	readonly IAudioManager _audioManager;
	readonly IPermissionEventSource _permissionEventSource;
	readonly IUserInputEventSource _userInputEventSource;
	readonly IAppSettingsFeature _appSettings;
	readonly ILogger<SoundFeature> _logger;
	const long MaxSoundFileSizeBytes = 10 * 1024 * 1024; // 10 MB

	readonly ConcurrentDictionary<string, byte[]> _soundBytes = new();

	// Default raw asset per sound name. Both "permission" and "userInput" share request.mp3.
	static readonly Dictionary<string, string> DefaultSoundAssets = new()
	{
		["permission"] = "Sounds/request.mp3",
		["userInput"] = "Sounds/request.mp3",
		["finished"] = "Sounds/finished.mp3"
	};

	static string CustomSoundPath(string soundName) =>
		Path.Combine(FileSystem.AppDataDirectory, "Sounds", $"{soundName}.mp3");

	static string GetSoundName(SoundEffectTypeEnum soundType) => soundType switch
	{
		SoundEffectTypeEnum.Permission => "permission",
		SoundEffectTypeEnum.UserInput => "userInput",
		SoundEffectTypeEnum.Finished => "finished",
		_ => "finished"
	};

	bool GetEnabled(SoundEffectTypeEnum soundType) => soundType switch
	{
		SoundEffectTypeEnum.Permission => _appSettings.SoundPermissionEnabled,
		SoundEffectTypeEnum.UserInput => _appSettings.SoundUserInputEnabled,
		SoundEffectTypeEnum.Finished => _appSettings.SoundFinishedEnabled,
		_ => false
	};

	double GetVolume(SoundEffectTypeEnum soundType) => soundType switch
	{
		SoundEffectTypeEnum.Permission => _appSettings.SoundPermissionVolume,
		SoundEffectTypeEnum.UserInput => _appSettings.SoundUserInputVolume,
		SoundEffectTypeEnum.Finished => _appSettings.SoundFinishedVolume,
		_ => 0.5
	};

	public SoundFeature(
		IAudioManager audioManager,
		IPermissionEventSource permissionEventSource,
		IUserInputEventSource userInputEventSource,
		IAppSettingsFeature appSettings,
		ILogger<SoundFeature> logger)
	{
		_audioManager = audioManager;
		_permissionEventSource = permissionEventSource;
		_userInputEventSource = userInputEventSource;
		_appSettings = appSettings;
		_logger = logger;

		_permissionEventSource.OnPermissionRequested += OnPermissionRequested;
		_userInputEventSource.OnUserInputRequested += OnUserInputRequested;
		SessionIdleHandler.OnSessionFinished += OnSessionFinished;

		_ = LoadAllSoundsAsync();
	}

	async Task LoadAllSoundsAsync()
	{
		await Task.WhenAll(DefaultSoundAssets.Keys.Select(LoadSingleSoundAsync));
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
				using Stream stream = await FileSystem.OpenAppPackageFileAsync(DefaultSoundAssets[soundName]);
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

	/// <summary>
	/// Saves <paramref name="stream"/> as the custom sound for <paramref name="soundType"/>.
	/// Throws <see cref="InvalidOperationException"/> if the stream exceeds the 10 MB size limit.
	/// </summary>
	public async Task SetCustomSoundAsync(SoundEffectTypeEnum soundType, Stream stream, string displayFileName)
	{
		if(stream.CanSeek && stream.Length > MaxSoundFileSizeBytes)
		{
			throw new InvalidOperationException("The selected audio file exceeds the 10 MB size limit.");
		}

		string soundName = GetSoundName(soundType);
		string customDir = Path.Combine(FileSystem.AppDataDirectory, "Sounds");
		string customPath = CustomSoundPath(soundName);
		Directory.CreateDirectory(customDir);
		try
		{
			await using FileStream fs = File.Create(customPath);
			await CopyToAsyncWithSizeLimit(stream, fs, MaxSoundFileSizeBytes);
		}
		catch
		{
			if(File.Exists(customPath))
			{
				File.Delete(customPath);
			}
			throw;
		}
		StoreCustomFileName(soundType, displayFileName);
		await LoadSingleSoundAsync(soundName);
	}
	static async Task CopyToAsyncWithSizeLimit(Stream source, Stream destination, long sizeLimitBytes, CancellationToken cancellationToken = default)
	{
		byte[] buffer = new byte[81920];
		long totalBytesRead = 0;
		while(true)
		{
			int bytesRead = await source.ReadAsync(buffer, cancellationToken);
			if(bytesRead == 0)
			{
				break;
			}
			totalBytesRead += bytesRead;
			if(totalBytesRead > sizeLimitBytes)
			{
				throw new InvalidOperationException("The selected audio file exceeds the 10 MB size limit.");
			}
			await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
		}
	}

	/// <summary>
	/// Removes any custom sound for <paramref name="soundType"/> and reverts to the bundled default.
	/// </summary>
	public async Task ResetToDefaultAsync(SoundEffectTypeEnum soundType)
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

	/// <summary>
	/// Returns the user-supplied file name, or an empty string when using the default.
	/// </summary>
	public string GetCustomFileName(SoundEffectTypeEnum soundType) => soundType switch
	{
		SoundEffectTypeEnum.Permission => _appSettings.SoundPermissionCustomFileName,
		SoundEffectTypeEnum.UserInput => _appSettings.SoundUserInputCustomFileName,
		SoundEffectTypeEnum.Finished => _appSettings.SoundFinishedCustomFileName,
		_ => string.Empty
	};

	void StoreCustomFileName(SoundEffectTypeEnum soundType, string fileName)
	{
		switch(soundType)
		{
			case SoundEffectTypeEnum.Permission: _appSettings.SoundPermissionCustomFileName = fileName; break;
			case SoundEffectTypeEnum.UserInput: _appSettings.SoundUserInputCustomFileName = fileName; break;
			case SoundEffectTypeEnum.Finished: _appSettings.SoundFinishedCustomFileName = fileName; break;
		}
	}

	void OnPermissionRequested(string sessionId, PermissionRequestModel request) =>
		_ = PlaySoundAsync(SoundEffectTypeEnum.Permission);

	void OnUserInputRequested(string sessionId, UserInputRequestModel request) =>
		_ = PlaySoundAsync(SoundEffectTypeEnum.UserInput);

	void OnSessionFinished() => _ = PlaySoundAsync(SoundEffectTypeEnum.Finished);

	/// <summary>
	/// Plays a sound. Pass <paramref name="forPreview"/> = <c>true</c> from the settings page
	/// to bypass the per-sound enabled toggle.
	/// </summary>
	public Task PlaySoundAsync(SoundEffectTypeEnum soundType, bool forPreview = false)
	{
		if(!forPreview && !GetEnabled(soundType))
		{
			return Task.CompletedTask;
		}

		string soundName = GetSoundName(soundType);
		if(!_soundBytes.TryGetValue(soundName, out byte[]? bytes))
		{
			return Task.CompletedTask;
		}

		double volume = GetVolume(soundType);

		try
		{
			IAudioPlayer player = _audioManager.CreatePlayer(new MemoryStream(bytes));
			bool playbackStarted = false;

			void OnEnded(object? s, EventArgs e)
			{
				player.PlaybackEnded -= OnEnded;
				player.Dispose();
			}

			try
			{
				player.Volume = volume;
				player.PlaybackEnded += OnEnded;
				player.Play();
				playbackStarted = true;
			}
			catch
			{
				if(!playbackStarted)
				{
					player.PlaybackEnded -= OnEnded;
					player.Dispose();
				}
				throw;
			}
		}
		catch(Exception ex)
		{
			_logger.LogDebug(ex, "Failed to play sound effect {SoundType}", soundType);
		}

		return Task.CompletedTask;
	}

}
