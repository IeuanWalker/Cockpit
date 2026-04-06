using System.Globalization;
using Cockpit.Features.AppSettings;
using Cockpit.Features.Sounds;
using Cockpit.Features.UIState;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Popups.Settings;

public partial class SoundSettings : ComponentBase, IDisposable
{
	readonly UIStateFeature _uiStateFeature;
	readonly IAppSettingsFeature _appSettingsFeature;
	readonly SoundFeature _soundFeature;

	public SoundSettings(
		UIStateFeature uiState,
		IAppSettingsFeature appSettings,
		SoundFeature soundFeature)
	{
		_uiStateFeature = uiState;
		_appSettingsFeature = appSettings;
		_soundFeature = soundFeature;
	}

	bool _permissionEnabled;
	float _permissionVolume;
	string _permissionCustomFile = string.Empty;

	bool _userInputEnabled;
	float _userInputVolume;
	string _userInputCustomFile = string.Empty;

	bool _finishedEnabled;
	float _finishedVolume;
	string _finishedCustomFile = string.Empty;

	protected override void OnInitialized()
	{
		_permissionEnabled = _appSettingsFeature.SoundPermissionEnabled;
		_permissionVolume = _appSettingsFeature.SoundPermissionVolume;
		_permissionCustomFile = _soundFeature.GetCustomFileName(SoundEffectType.Permission);

		_userInputEnabled = _appSettingsFeature.SoundUserInputEnabled;
		_userInputVolume = _appSettingsFeature.SoundUserInputVolume;
		_userInputCustomFile = _soundFeature.GetCustomFileName(SoundEffectType.UserInput);

		_finishedEnabled = _appSettingsFeature.SoundFinishedEnabled;
		_finishedVolume = _appSettingsFeature.SoundFinishedVolume;
		_finishedCustomFile = _soundFeature.GetCustomFileName(SoundEffectType.Finished);

		_uiStateFeature.OnStateChanged += OnStateChanged;
	}

	void SetEnabled(SoundEffectType soundType, bool enabled)
	{
		switch(soundType)
		{
			case SoundEffectType.Permission:
				_permissionEnabled = enabled;
				_appSettingsFeature.SoundPermissionEnabled = enabled;
				break;
			case SoundEffectType.UserInput:
				_userInputEnabled = enabled;
				_appSettingsFeature.SoundUserInputEnabled = enabled;
				break;
			case SoundEffectType.Finished:
				_finishedEnabled = enabled;
				_appSettingsFeature.SoundFinishedEnabled = enabled;
				break;
		}
	}

	void OnVolumeChanged(ChangeEventArgs e, SoundEffectType soundType)
	{
		if(!float.TryParse(e.Value?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
		{
			return;
		}

		switch(soundType)
		{
			case SoundEffectType.Permission:
				_permissionVolume = value;
				_appSettingsFeature.SoundPermissionVolume = value;
				break;
			case SoundEffectType.UserInput:
				_userInputVolume = value;
				_appSettingsFeature.SoundUserInputVolume = value;
				break;
			case SoundEffectType.Finished:
				_finishedVolume = value;
				_appSettingsFeature.SoundFinishedVolume = value;
				break;
		}
	}

	async Task UploadSound(SoundEffectType soundType)
	{
		FilePickerFileType mp3Type = new(new Dictionary<DevicePlatform, IEnumerable<string>>
		{
			{ DevicePlatform.WinUI, [".mp3"] },
			{ DevicePlatform.MacCatalyst, ["public.mp3"] }
		});

		FileResult? result = await MainThread.InvokeOnMainThreadAsync(() =>
			FilePicker.Default.PickAsync(new PickOptions
			{
				FileTypes = mp3Type,
				PickerTitle = "Select MP3 file"
			}));

		if(result is null)
		{
			return;
		}

		using Stream stream = await result.OpenReadAsync();
		await _soundFeature.SetCustomSoundAsync(soundType, stream, result.FileName);

		switch(soundType)
		{
			case SoundEffectType.Permission: _permissionCustomFile = result.FileName; break;
			case SoundEffectType.UserInput: _userInputCustomFile = result.FileName; break;
			case SoundEffectType.Finished: _finishedCustomFile = result.FileName; break;
		}

		await InvokeAsync(StateHasChanged);
	}

	async Task ResetSound(SoundEffectType soundType)
	{
		await _soundFeature.ResetToDefaultAsync(soundType);

		switch(soundType)
		{
			case SoundEffectType.Permission: _permissionCustomFile = string.Empty; break;
			case SoundEffectType.UserInput: _userInputCustomFile = string.Empty; break;
			case SoundEffectType.Finished: _finishedCustomFile = string.Empty; break;
		}

		await InvokeAsync(StateHasChanged);
	}

	async Task Preview(SoundEffectType soundType)
	{
		await _soundFeature.PlaySoundAsync(soundType, forPreview: true);
	}

	void OnStateChanged()
	{
		InvokeAsync(StateHasChanged);
	}

	public void Dispose()
	{
		Dispose(true);
		GC.SuppressFinalize(this);
	}

	protected virtual void Dispose(bool disposing)
	{
		if(disposing)
		{
			_uiStateFeature.OnStateChanged -= OnStateChanged;
		}
	}
}
