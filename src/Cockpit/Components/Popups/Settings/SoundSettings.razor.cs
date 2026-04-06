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
		_permissionCustomFile = _soundFeature.GetCustomFileName(SoundEffectTypeEnum.Permission);

		_userInputEnabled = _appSettingsFeature.SoundUserInputEnabled;
		_userInputVolume = _appSettingsFeature.SoundUserInputVolume;
		_userInputCustomFile = _soundFeature.GetCustomFileName(SoundEffectTypeEnum.UserInput);

		_finishedEnabled = _appSettingsFeature.SoundFinishedEnabled;
		_finishedVolume = _appSettingsFeature.SoundFinishedVolume;
		_finishedCustomFile = _soundFeature.GetCustomFileName(SoundEffectTypeEnum.Finished);

		_uiStateFeature.OnStateChanged += OnStateChanged;
	}

	void SetEnabled(SoundEffectTypeEnum soundType, bool enabled)
	{
		switch(soundType)
		{
			case SoundEffectTypeEnum.Permission:
				_permissionEnabled = enabled;
				_appSettingsFeature.SoundPermissionEnabled = enabled;
				break;
			case SoundEffectTypeEnum.UserInput:
				_userInputEnabled = enabled;
				_appSettingsFeature.SoundUserInputEnabled = enabled;
				break;
			case SoundEffectTypeEnum.Finished:
				_finishedEnabled = enabled;
				_appSettingsFeature.SoundFinishedEnabled = enabled;
				break;
		}
	}

	void OnVolumeChanged(ChangeEventArgs e, SoundEffectTypeEnum soundType)
	{
		if(!float.TryParse(e.Value?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
		{
			return;
		}

		switch(soundType)
		{
			case SoundEffectTypeEnum.Permission:
				_permissionVolume = value;
				_appSettingsFeature.SoundPermissionVolume = value;
				break;
			case SoundEffectTypeEnum.UserInput:
				_userInputVolume = value;
				_appSettingsFeature.SoundUserInputVolume = value;
				break;
			case SoundEffectTypeEnum.Finished:
				_finishedVolume = value;
				_appSettingsFeature.SoundFinishedVolume = value;
				break;
		}
	}

	async Task UploadSound(SoundEffectTypeEnum soundType)
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
			case SoundEffectTypeEnum.Permission: _permissionCustomFile = result.FileName; break;
			case SoundEffectTypeEnum.UserInput: _userInputCustomFile = result.FileName; break;
			case SoundEffectTypeEnum.Finished: _finishedCustomFile = result.FileName; break;
		}

		await InvokeAsync(StateHasChanged);
	}

	async Task ResetSound(SoundEffectTypeEnum soundType)
	{
		await _soundFeature.ResetToDefaultAsync(soundType);

		switch(soundType)
		{
			case SoundEffectTypeEnum.Permission: _permissionCustomFile = string.Empty; break;
			case SoundEffectTypeEnum.UserInput: _userInputCustomFile = string.Empty; break;
			case SoundEffectTypeEnum.Finished: _finishedCustomFile = string.Empty; break;
		}

		await InvokeAsync(StateHasChanged);
	}

	async Task Preview(SoundEffectTypeEnum soundType)
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
