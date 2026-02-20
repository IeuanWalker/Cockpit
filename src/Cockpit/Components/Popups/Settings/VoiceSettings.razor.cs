using Cockpit.Features.TextToSpeech;
using Cockpit.Services;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components.Popups.Settings;

public partial class VoiceSettings : ComponentBase, IDisposable
{
	[Inject] UIStateService _uiState { get; set; } = default!;
	[Inject] TextToSpeechFeature _textToSpeechFeature { get; set; } = default!;

	IEnumerable<Locale> _locales = [];
	float _volume = UserAppSettings.VoiceVolume;
	float _pitch = UserAppSettings.VoicePitch;
	float _rate = UserAppSettings.VoiceRate;
	string _localeId = UserAppSettings.VoiceLocale;

	protected override void OnInitialized()
	{
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
		if(float.TryParse(e.Value?.ToString(), out float value))
		{
			_volume = value;
			UserAppSettings.VoiceVolume = value;
		}
	}

	void OnPitchChanged(ChangeEventArgs e)
	{
		if(float.TryParse(e.Value?.ToString(), out float value))
		{
			_pitch = value;
			UserAppSettings.VoicePitch = value;
		}
	}

	void OnRateChanged(ChangeEventArgs e)
	{
		if(float.TryParse(e.Value?.ToString(), out float value))
		{
			_rate = value;
			UserAppSettings.VoiceRate = value;
		}
	}

	void OnLocaleChanged(ChangeEventArgs e)
	{
		_localeId = e.Value?.ToString() ?? string.Empty;
		UserAppSettings.VoiceLocale = _localeId;
	}

	void ResetToDefaults()
	{
		_volume = TextToSpeechFeature.DefaultVoiceVolume;
		_pitch = TextToSpeechFeature.DefaultVoicePitch;
		_rate = TextToSpeechFeature.DefaultVoiceRate;
		UserAppSettings.VoiceVolume = _volume;
		UserAppSettings.VoicePitch = _pitch;
		UserAppSettings.VoiceRate = _rate;
		UserAppSettings.VoiceLocale = string.Empty;
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