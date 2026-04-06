using System.Collections.Concurrent;
using Cockpit.Features.AppSettings;
using Cockpit.Features.Permissions;
using Cockpit.Features.Permissions.Models;
using Cockpit.Features.SessionEvents.Handlers;
using Cockpit.Features.UserInputRequests;
using Microsoft.Extensions.Logging;
using Plugin.Maui.Audio;

namespace Cockpit.Features.Sounds;

public sealed partial class SoundFeature : IDisposable
{
	readonly IAudioManager _audioManager;
	readonly PermissionFeature _permissionFeature;
	readonly UserInputFeature _userInputFeature;
	readonly IAppSettingsFeature _appSettings;
	readonly ILogger<SoundFeature> _logger;
	const long maxSoundFileSizeBytes = 10 * 1024 * 1024; // 10 MB

	readonly ConcurrentDictionary<string, byte[]> _soundBytes = new();

	// Default raw asset per sound name. Both "permission" and "userInput" fall back to request.mp3.
	static readonly Dictionary<string, string> defaultSoundAssets = new()
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

	public SoundFeature(
		IAudioManager audioManager,
		PermissionFeature permissionFeature,
		UserInputFeature userInputFeature,
		IAppSettingsFeature appSettings,
		ILogger<SoundFeature> logger)
	{
		_audioManager = audioManager;
		_permissionFeature = permissionFeature;
		_userInputFeature = userInputFeature;
		_appSettings = appSettings;
		_logger = logger;

		_permissionFeature.OnPermissionRequested += OnPermissionRequested;
		_userInputFeature.OnUserInputRequested += OnUserInputRequested;
		SessionIdleHandler.OnSessionFinished += OnSessionFinished;

		_ = LoadAllSoundsAsync();
	}

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

	/// <summary>
	/// Saves <paramref name="stream"/> as the custom sound for <paramref name="soundType"/>.
	/// Throws <see cref="InvalidOperationException"/> if the stream exceeds the 10 MB size limit.
	/// </summary>
	public async Task SetCustomSoundAsync(SoundEffectTypeEnum soundType, Stream stream, string displayFileName)
	{
		if(stream.CanSeek && stream.Length > maxSoundFileSizeBytes)
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
			await CopyToAsyncWithSizeLimit(stream, fs, maxSoundFileSizeBytes);
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
	/// Plays a sound. Pass <paramref name="forPreview"/> = <c>true</c> from the settings
	/// page to bypass the per-sound enabled toggle.
	/// </summary>
	public async Task PlaySoundAsync(SoundEffectTypeEnum soundType, bool forPreview = false)
	{
		bool enabled = soundType switch
		{
			SoundEffectTypeEnum.Permission => _appSettings.SoundPermissionEnabled,
			SoundEffectTypeEnum.UserInput => _appSettings.SoundUserInputEnabled,
			SoundEffectTypeEnum.Finished => _appSettings.SoundFinishedEnabled,
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
			SoundEffectTypeEnum.Permission => _appSettings.SoundPermissionVolume,
			SoundEffectTypeEnum.UserInput => _appSettings.SoundUserInputVolume,
			SoundEffectTypeEnum.Finished => _appSettings.SoundFinishedVolume,
			_ => 0.5
		};

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
	}

	public void Dispose()
	{
		_permissionFeature.OnPermissionRequested -= OnPermissionRequested;
		_userInputFeature.OnUserInputRequested -= OnUserInputRequested;
		SessionIdleHandler.OnSessionFinished -= OnSessionFinished;
	}
}
