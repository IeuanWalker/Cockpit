using System.Collections.Concurrent;
using System.Reflection;
using Cockpit.Features.AppSettings;
using Cockpit.Features.Permissions;
using Cockpit.Features.Permissions.Models;
using Cockpit.Features.SessionEvents.Handlers;
using Cockpit.Features.Sounds;
using Cockpit.Features.UserInputRequests;
using Cockpit.UnitTests.Features.AppSettings;
using Microsoft.Extensions.Logging.Abstractions;
using Plugin.Maui.Audio;
using Shouldly;

namespace Cockpit.UnitTests.Features.Sounds;

/// <summary>
/// Unit tests for <see cref="SoundFeature"/>.
/// Uses in-memory fakes for all I/O — no MAUI platform required.
/// </summary>
public class SoundFeatureTests
{
	// ─────────────────────────────────────────────────────────────────────────
	// Helpers
	// ─────────────────────────────────────────────────────────────────────────

	static IAppSettingsFeature CreateSettings(Action<IAppSettingsFeature>? configure = null)
	{
		IAppSettingsFeature s = new AppSettingsFeature(new UserAppSettings(new InMemoryPreferencesStorage()));
		configure?.Invoke(s);
		return s;
	}

	/// <summary>
	/// Constructs a <see cref="SoundFeature"/> whose sound-bytes dictionary has been
	/// pre-populated with dummy audio data so that <see cref="SoundFeature.PlaySoundAsync"/>
	/// can reach the audio-player creation path without hitting the file system.
	/// </summary>
	static SoundFeature CreateFeatureWithLoadedSounds(
		FakeAudioManager audioManager,
		IAppSettingsFeature settings,
		FakePermissionEventSource? permissionSource = null,
		FakeUserInputEventSource? userInputSource = null)
	{
		SoundFeature feature = new(
			audioManager,
			permissionSource ?? new FakePermissionEventSource(),
			userInputSource ?? new FakeUserInputEventSource(),
			settings,
			NullLogger<SoundFeature>.Instance);

		// Pre-populate _soundBytes via reflection so tests don't need the file system.
		ConcurrentDictionary<string, byte[]> soundBytes = GetSoundBytes(feature);
		soundBytes["permission"] = [1, 2, 3];
		soundBytes["userInput"] = [4, 5, 6];
		soundBytes["finished"] = [7, 8, 9];

		return feature;
	}

	static ConcurrentDictionary<string, byte[]> GetSoundBytes(SoundFeature feature)
	{
		FieldInfo field = typeof(SoundFeature)
			.GetField("_soundBytes", BindingFlags.NonPublic | BindingFlags.Instance)!;
		return (ConcurrentDictionary<string, byte[]>)field.GetValue(feature)!;
	}

	// ─────────────────────────────────────────────────────────────────────────
	// PlaySoundAsync — enabled/disabled gate
	// ─────────────────────────────────────────────────────────────────────────

	[Theory]
	[InlineData(SoundEffectTypeEnum.Permission)]
	[InlineData(SoundEffectTypeEnum.UserInput)]
	[InlineData(SoundEffectTypeEnum.Finished)]
	public async Task PlaySoundAsync_WhenSoundDisabled_DoesNotCreatePlayer(SoundEffectTypeEnum soundType)
	{
		IAppSettingsFeature settings = CreateSettings(s =>
		{
			s.SoundPermissionEnabled = false;
			s.SoundUserInputEnabled = false;
			s.SoundFinishedEnabled = false;
		});
		FakeAudioManager audioManager = new();
		using SoundFeature feature = CreateFeatureWithLoadedSounds(audioManager, settings);

		await feature.PlaySoundAsync(soundType, forPreview: false);

		audioManager.CreatePlayerCallCount.ShouldBe(0);
	}

	[Theory]
	[InlineData(SoundEffectTypeEnum.Permission)]
	[InlineData(SoundEffectTypeEnum.UserInput)]
	[InlineData(SoundEffectTypeEnum.Finished)]
	public async Task PlaySoundAsync_WhenSoundDisabledButForPreview_CreatesPlayer(SoundEffectTypeEnum soundType)
	{
		IAppSettingsFeature settings = CreateSettings(s =>
		{
			s.SoundPermissionEnabled = false;
			s.SoundUserInputEnabled = false;
			s.SoundFinishedEnabled = false;
		});
		FakeAudioManager audioManager = new();
		using SoundFeature feature = CreateFeatureWithLoadedSounds(audioManager, settings);

		await feature.PlaySoundAsync(soundType, forPreview: true);

		audioManager.CreatePlayerCallCount.ShouldBe(1);
	}

	[Theory]
	[InlineData(SoundEffectTypeEnum.Permission)]
	[InlineData(SoundEffectTypeEnum.UserInput)]
	[InlineData(SoundEffectTypeEnum.Finished)]
	public async Task PlaySoundAsync_WhenSoundEnabled_CreatesAndPlaysPlayer(SoundEffectTypeEnum soundType)
	{
		IAppSettingsFeature settings = CreateSettings(s =>
		{
			s.SoundPermissionEnabled = true;
			s.SoundUserInputEnabled = true;
			s.SoundFinishedEnabled = true;
		});
		FakeAudioManager audioManager = new();
		using SoundFeature feature = CreateFeatureWithLoadedSounds(audioManager, settings);

		await feature.PlaySoundAsync(soundType, forPreview: false);

		audioManager.CreatePlayerCallCount.ShouldBe(1);
		audioManager.LastPlayer!.PlayCalled.ShouldBeTrue();
	}

	[Fact]
	public async Task PlaySoundAsync_WhenNoBytesLoaded_DoesNotCreatePlayer()
	{
		FakeAudioManager audioManager = new();
		IAppSettingsFeature settings = CreateSettings(s => s.SoundFinishedEnabled = true);
		SoundFeature feature = new(
			audioManager,
			new FakePermissionEventSource(),
			new FakeUserInputEventSource(),
			settings,
			NullLogger<SoundFeature>.Instance);

		await feature.PlaySoundAsync(SoundEffectTypeEnum.Finished, forPreview: false);

		audioManager.CreatePlayerCallCount.ShouldBe(0);
	}

	// ─────────────────────────────────────────────────────────────────────────
	// PlaySoundAsync — volume is applied
	// ─────────────────────────────────────────────────────────────────────────

	[Theory]
	[InlineData(SoundEffectTypeEnum.Permission, 0.3f)]
	[InlineData(SoundEffectTypeEnum.UserInput, 0.7f)]
	[InlineData(SoundEffectTypeEnum.Finished, 1.0f)]
	public async Task PlaySoundAsync_AppliesCorrectVolume(SoundEffectTypeEnum soundType, float volume)
	{
		IAppSettingsFeature settings = CreateSettings(s =>
		{
			s.SoundPermissionEnabled = true;
			s.SoundUserInputEnabled = true;
			s.SoundFinishedEnabled = true;
			s.SoundPermissionVolume = volume;
			s.SoundUserInputVolume = volume;
			s.SoundFinishedVolume = volume;
		});
		FakeAudioManager audioManager = new();
		using SoundFeature feature = CreateFeatureWithLoadedSounds(audioManager, settings);

		await feature.PlaySoundAsync(soundType, forPreview: false);

		audioManager.LastPlayer!.Volume.ShouldBe(volume, tolerance: 0.001);
	}

	// ─────────────────────────────────────────────────────────────────────────
	// GetCustomFileName — routing
	// ─────────────────────────────────────────────────────────────────────────

	[Theory]
	[InlineData(SoundEffectTypeEnum.Permission, "permission.mp3")]
	[InlineData(SoundEffectTypeEnum.UserInput, "userInput.mp3")]
	[InlineData(SoundEffectTypeEnum.Finished, "finished.mp3")]
	public void GetCustomFileName_ReturnsValueFromMatchingSettingsProperty(SoundEffectTypeEnum soundType, string fileName)
	{
		IAppSettingsFeature settings = CreateSettings(s =>
		{
			s.SoundPermissionCustomFileName = "permission.mp3";
			s.SoundUserInputCustomFileName = "userInput.mp3";
			s.SoundFinishedCustomFileName = "finished.mp3";
		});
		using SoundFeature feature = new(
			new FakeAudioManager(),
			new FakePermissionEventSource(),
			new FakeUserInputEventSource(),
			settings,
			NullLogger<SoundFeature>.Instance);

		string result = feature.GetCustomFileName(soundType);

		result.ShouldBe(fileName);
	}

	[Fact]
	public void GetCustomFileName_WhenNoneSet_ReturnsEmpty()
	{
		IAppSettingsFeature settings = CreateSettings();
		using SoundFeature feature = new(
			new FakeAudioManager(),
			new FakePermissionEventSource(),
			new FakeUserInputEventSource(),
			settings,
			NullLogger<SoundFeature>.Instance);

		feature.GetCustomFileName(SoundEffectTypeEnum.Permission).ShouldBe(string.Empty);
		feature.GetCustomFileName(SoundEffectTypeEnum.UserInput).ShouldBe(string.Empty);
		feature.GetCustomFileName(SoundEffectTypeEnum.Finished).ShouldBe(string.Empty);
	}

	// ─────────────────────────────────────────────────────────────────────────
	// Event routing — OnPermissionRequested fires PlaySoundAsync(Permission)
	// ─────────────────────────────────────────────────────────────────────────

	[Fact]
	public async Task OnPermissionRequested_WhenEnabled_PlaysPermissionSound()
	{
		IAppSettingsFeature settings = CreateSettings(s => s.SoundPermissionEnabled = true);
		FakeAudioManager audioManager = new();
		FakePermissionEventSource permissionSource = new();
		using SoundFeature feature = CreateFeatureWithLoadedSounds(audioManager, settings, permissionSource: permissionSource);

		permissionSource.RaisePermissionRequested("session1", MakePermissionRequest());
		await Task.Yield(); // allow any queued async work to proceed

		audioManager.CreatePlayerCallCount.ShouldBe(1);
	}

	[Fact]
	public async Task OnPermissionRequested_WhenDisabled_DoesNotPlay()
	{
		IAppSettingsFeature settings = CreateSettings(s => s.SoundPermissionEnabled = false);
		FakeAudioManager audioManager = new();
		FakePermissionEventSource permissionSource = new();
		using SoundFeature feature = CreateFeatureWithLoadedSounds(audioManager, settings, permissionSource: permissionSource);

		permissionSource.RaisePermissionRequested("session1", MakePermissionRequest());
		await Task.Yield();

		audioManager.CreatePlayerCallCount.ShouldBe(0);
	}

	[Fact]
	public async Task OnUserInputRequested_WhenEnabled_PlaysUserInputSound()
	{
		IAppSettingsFeature settings = CreateSettings(s => s.SoundUserInputEnabled = true);
		FakeAudioManager audioManager = new();
		FakeUserInputEventSource userInputSource = new();
		using SoundFeature feature = CreateFeatureWithLoadedSounds(audioManager, settings, userInputSource: userInputSource);

		userInputSource.RaiseUserInputRequested("session1", new UserInputRequestModel
		{
			SessionId = "session1",
			Question = "q",
			FullRequestJson = "{}"
		});
		await Task.Yield();

		audioManager.CreatePlayerCallCount.ShouldBe(1);
	}

	[Fact]
	public async Task OnSessionFinished_WhenEnabled_PlaysFinishedSound()
	{
		IAppSettingsFeature settings = CreateSettings(s => s.SoundFinishedEnabled = true);
		FakeAudioManager audioManager = new();
		using SoundFeature feature = CreateFeatureWithLoadedSounds(audioManager, settings);

		// Fire the static event via reflection.
		FireStaticSessionFinished();
		await Task.Yield();

		audioManager.CreatePlayerCallCount.ShouldBe(1);
	}

	// ─────────────────────────────────────────────────────────────────────────
	// Dispose — unsubscribes all events
	// ─────────────────────────────────────────────────────────────────────────

	[Fact]
	public async Task Dispose_UnsubscribesPermissionEvent()
	{
		IAppSettingsFeature settings = CreateSettings(s => s.SoundPermissionEnabled = true);
		FakeAudioManager audioManager = new();
		FakePermissionEventSource permissionSource = new();
		SoundFeature feature = CreateFeatureWithLoadedSounds(audioManager, settings, permissionSource: permissionSource);

		feature.Dispose();
		permissionSource.RaisePermissionRequested("session1", MakePermissionRequest());
		await Task.Yield();

		audioManager.CreatePlayerCallCount.ShouldBe(0);
	}

	[Fact]
	public async Task Dispose_UnsubscribesUserInputEvent()
	{
		IAppSettingsFeature settings = CreateSettings(s => s.SoundUserInputEnabled = true);
		FakeAudioManager audioManager = new();
		FakeUserInputEventSource userInputSource = new();
		SoundFeature feature = CreateFeatureWithLoadedSounds(audioManager, settings, userInputSource: userInputSource);

		feature.Dispose();
		userInputSource.RaiseUserInputRequested("session1", new UserInputRequestModel
		{
			SessionId = "session1",
			Question = "q",
			FullRequestJson = "{}"
		});
		await Task.Yield();

		audioManager.CreatePlayerCallCount.ShouldBe(0);
	}

	[Fact]
	public async Task Dispose_UnsubscribesSessionFinishedEvent()
	{
		IAppSettingsFeature settings = CreateSettings(s => s.SoundFinishedEnabled = true);
		FakeAudioManager audioManager = new();
		SoundFeature feature = CreateFeatureWithLoadedSounds(audioManager, settings);

		feature.Dispose();
		FireStaticSessionFinished();
		await Task.Yield();

		audioManager.CreatePlayerCallCount.ShouldBe(0);
	}

	// ─────────────────────────────────────────────────────────────────────────
	// Player disposal — OnEnded disposes the player
	// ─────────────────────────────────────────────────────────────────────────

	[Fact]
	public async Task PlaySoundAsync_OnPlaybackEnded_DisposesPlayer()
	{
		IAppSettingsFeature settings = CreateSettings(s => s.SoundFinishedEnabled = true);
		FakeAudioManager audioManager = new();
		using SoundFeature feature = CreateFeatureWithLoadedSounds(audioManager, settings);

		await feature.PlaySoundAsync(SoundEffectTypeEnum.Finished);

		FakeAudioPlayer player = audioManager.LastPlayer!;
		player.SimulatePlaybackEnded();

		player.IsDisposed.ShouldBeTrue();
	}

	// ─────────────────────────────────────────────────────────────────────────
	// Helpers
	// ─────────────────────────────────────────────────────────────────────────

	static PermissionRequestModel MakePermissionRequest() => new()
	{
		SessionId = "session1",
		FullCommand = "cmd",
		Commands = ["cmd"],
		RequestTitle = "title",
		Intention = "intent",
		CanApproveGlobally = false,
		CanApproveForSession = false,
		FullRequestJson = "{}"
	};

	static void FireStaticSessionFinished()
	{
		FieldInfo? field = typeof(SessionIdleHandler)
			.GetField("OnSessionFinished", BindingFlags.NonPublic | BindingFlags.Static);
		if(field is null)
		{
			return;
		}

		Action? handler = (Action?)field.GetValue(null);
		handler?.Invoke();
	}
}

// ─────────────────────────────────────────────────────────────────────────────
// Test doubles
// ─────────────────────────────────────────────────────────────────────────────

sealed class FakePermissionEventSource : IPermissionEventSource
{
	public event Action<string, PermissionRequestModel>? OnPermissionRequested;

	public void RaisePermissionRequested(string sessionId, PermissionRequestModel model) =>
		OnPermissionRequested?.Invoke(sessionId, model);
}

sealed class FakeUserInputEventSource : IUserInputEventSource
{
	public event Action<string, UserInputRequestModel>? OnUserInputRequested;

	public void RaiseUserInputRequested(string sessionId, UserInputRequestModel model) =>
		OnUserInputRequested?.Invoke(sessionId, model);
}

sealed class FakeAudioManager : IAudioManager
{
	public int CreatePlayerCallCount { get; private set; }
	public FakeAudioPlayer? LastPlayer { get; private set; }

	public AudioPlayerOptions DefaultPlayerOptions { get; set; } = new();
	public AudioRecorderOptions DefaultRecorderOptions { get; set; } = new();
	public AudioStreamOptions DefaultStreamerOptions { get; set; } = new();

	public IAudioPlayer CreatePlayer(AudioPlayerOptions? options = null)
	{
		LastPlayer = new FakeAudioPlayer();
		CreatePlayerCallCount++;
		return LastPlayer;
	}

	public IAudioPlayer CreatePlayer(Stream stream, AudioPlayerOptions? options = null)
	{
		LastPlayer = new FakeAudioPlayer();
		CreatePlayerCallCount++;
		return LastPlayer;
	}

	public IAudioPlayer CreatePlayer(string fileName, AudioPlayerOptions? options = null)
	{
		LastPlayer = new FakeAudioPlayer();
		CreatePlayerCallCount++;
		return LastPlayer;
	}

	public AsyncAudioPlayer CreateAsyncPlayer(Stream stream, AudioPlayerOptions? options = null) =>
		throw new NotSupportedException();

	public AsyncAudioPlayer CreateAsyncPlayer(string fileName, AudioPlayerOptions? options = null) =>
		throw new NotSupportedException();

	public IAudioRecorder CreateRecorder(AudioRecorderOptions? options = null) =>
		throw new NotSupportedException();

	public IAudioStreamer CreateStreamer() =>
		throw new NotSupportedException();
}

sealed class FakeAudioPlayer : IAudioPlayer
{
	public bool PlayCalled { get; private set; }
	public bool IsDisposed { get; private set; }

	// IAudio
	public double Volume { get; set; }
	public double Balance { get; set; }
	public double Duration => 1.0;
	public double CurrentPosition => 0.0;
	public double Speed { get; set; } = 1.0;
	public double MinimumSpeed => 0.5;
	public double MaximumSpeed => 2.0;
	public bool CanSetSpeed => true;
	public bool IsPlaying { get; private set; }
	public bool Loop { get; set; }
	public bool CanSeek => false;

	public event EventHandler? PlaybackEnded;
#pragma warning disable CS0067
	public event EventHandler? Error;
#pragma warning restore CS0067

	public void Play()
	{
		PlayCalled = true;
		IsPlaying = true;
	}

	public void Pause() => IsPlaying = false;
	public void Stop() => IsPlaying = false;
	public void Seek(double position) { }
	public void SetSource(Stream stream) { }

	public void SimulatePlaybackEnded() => PlaybackEnded?.Invoke(this, EventArgs.Empty);

	public void Dispose()
	{
		IsDisposed = true;
	}
}
