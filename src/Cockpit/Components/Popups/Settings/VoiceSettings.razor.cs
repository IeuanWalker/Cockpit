using System.Globalization;
using Cockpit.Features.AppSettings;
using Cockpit.Features.TextToSpeech;
using Cockpit.Features.UIState;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Popups.Settings;

public partial class VoiceSettings : ComponentBase, IDisposable
{
	readonly UIStateFeature _uiStateFeature;
	readonly TextToSpeechFeature _textToSpeechFeature;
	readonly IAppSettingsFeature _appSettingsFeature;
	public VoiceSettings(
		UIStateFeature uiState,
		TextToSpeechFeature textToSpeechFeature,
		IAppSettingsFeature appSettings)
	{
		_uiStateFeature = uiState;
		_textToSpeechFeature = textToSpeechFeature;
		_appSettingsFeature = appSettings;
	}

	IEnumerable<Locale> _locales = [];
	float _volume;
	float _pitch;
	float _rate;
	string _localeId = string.Empty;

	protected override void OnInitialized()
	{
		_volume = _appSettingsFeature.VoiceVolume;
		_pitch = _appSettingsFeature.VoicePitch;
		_rate = _appSettingsFeature.VoiceRate;
		_localeId = _appSettingsFeature.VoiceLocale;
		_uiStateFeature.OnStateChanged += OnStateChanged;
	}

	protected override async Task OnAfterRenderAsync(bool firstRender)
	{
		if(firstRender)
		{
			_locales = await _textToSpeechFeature.GetLocales();
			await InvokeAsync(StateHasChanged);
		}
	}

	void OnVolumeChanged(ChangeEventArgs e)
	{
		if(float.TryParse(e.Value?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
		{
			_volume = value;
			_appSettingsFeature.VoiceVolume = value;
		}
	}

	void OnPitchChanged(ChangeEventArgs e)
	{
		if(float.TryParse(e.Value?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
		{
			_pitch = value;
			_appSettingsFeature.VoicePitch = value;
		}
	}

	void OnRateChanged(ChangeEventArgs e)
	{
		if(float.TryParse(e.Value?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
		{
			_rate = value;
			_appSettingsFeature.VoiceRate = value;
		}
	}

	void OnLocaleChanged(ChangeEventArgs e)
	{
		_localeId = e.Value?.ToString() ?? string.Empty;
		_appSettingsFeature.VoiceLocale = _localeId;
	}

	void ResetToDefaults()
	{
		_volume = TextToSpeechFeature.DefaultVoiceVolume;
		_pitch = TextToSpeechFeature.DefaultVoicePitch;
		_rate = TextToSpeechFeature.DefaultVoiceRate;
		_localeId = string.Empty;
		_appSettingsFeature.VoiceVolume = _volume;
		_appSettingsFeature.VoicePitch = _pitch;
		_appSettingsFeature.VoiceRate = _rate;
		_appSettingsFeature.VoiceLocale = _localeId;
	}

	async Task TestVoice()
	{
		await _textToSpeechFeature.Speak("__test__", "Hello! This is a test of the text to speech settings.");
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
