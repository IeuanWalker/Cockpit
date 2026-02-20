using System.Globalization;
using Cockpit.Features.AppSettings;
using Cockpit.Features.TextToSpeech;
using Cockpit.Features.UIState;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Popups.Settings;

public partial class VoiceSettings : ComponentBase, IDisposable
{
	[Inject] UIStateFeature _uiState { get; set; } = default!;
	[Inject] TextToSpeechFeature _textToSpeechFeature { get; set; } = default!;
	[Inject] IAppSettingsFeature _appSettings { get; set; } = default!;

	IEnumerable<Locale> _locales = [];
	float _volume;
	float _pitch;
	float _rate;
	string _localeId = string.Empty;

	protected override void OnInitialized()
	{
		_volume = _appSettings.VoiceVolume;
		_pitch = _appSettings.VoicePitch;
		_rate = _appSettings.VoiceRate;
		_localeId = _appSettings.VoiceLocale;
		_uiState.OnStateChanged += OnStateChanged;
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
			_appSettings.VoiceVolume = value;
		}
	}

	void OnPitchChanged(ChangeEventArgs e)
	{
		if(float.TryParse(e.Value?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
		{
			_pitch = value;
			_appSettings.VoicePitch = value;
		}
	}

	void OnRateChanged(ChangeEventArgs e)
	{
		if(float.TryParse(e.Value?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
		{
			_rate = value;
			_appSettings.VoiceRate = value;
		}
	}

	void OnLocaleChanged(ChangeEventArgs e)
	{
		_localeId = e.Value?.ToString() ?? string.Empty;
		_appSettings.VoiceLocale = _localeId;
	}

	void ResetToDefaults()
	{
		_volume = TextToSpeechFeature.DefaultVoiceVolume;
		_pitch = TextToSpeechFeature.DefaultVoicePitch;
		_rate = TextToSpeechFeature.DefaultVoiceRate;
		_localeId = string.Empty;
		_appSettings.VoiceVolume = _volume;
		_appSettings.VoicePitch = _pitch;
		_appSettings.VoiceRate = _rate;
		_appSettings.VoiceLocale = _localeId;
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
			_uiState.OnStateChanged -= OnStateChanged;
		}
	}
}
