using Cockpit.Features.AppSettings;
using Cockpit.Features.TextToSpeech;

namespace Cockpit.Features.UIState;

public class UIStateFeature
{
	readonly TextToSpeechFeature _textToSpeechFeature;
	readonly IAppSettingsFeature _appSettingsFeature;

	public UIStateFeature(TextToSpeechFeature textToSpeechFeature, IAppSettingsFeature appSettingsFeature)
	{
		_textToSpeechFeature = textToSpeechFeature;
		_appSettingsFeature = appSettingsFeature;

		_leftSidebarWidth = appSettingsFeature.LeftSidebarWidth;
		_rightSidebarWidth = appSettingsFeature.RightSidebarWidth;
		_sendOnEnter = appSettingsFeature.SendOnEnter;
		_textToSpeechEnabled = appSettingsFeature.TextToSpeechEnabled;
	}

	public event Action? OnStateChanged;

	public bool LeftSidebarCollapsed { get; private set; } = false;
	public bool RightSidebarCollapsed { get; private set; } = false;

	int _leftSidebarWidth;
	public int LeftSidebarWidth
	{
		get => _leftSidebarWidth;
		private set
		{
			_leftSidebarWidth = value;
			_appSettingsFeature.LeftSidebarWidth = value;
		}
	}

	int _rightSidebarWidth;
	public int RightSidebarWidth
	{
		get => _rightSidebarWidth;
		private set
		{
			_rightSidebarWidth = value;
			_appSettingsFeature.RightSidebarWidth = value;
		}
	}

	public bool SettingsPopupOpen { get; private set; } = false;

	public bool IsRecording { get; private set; } = false;

	public SettingsSectionEnum? PendingSettingsSection { get; private set; } = null;

	bool _sendOnEnter;
	public bool SendOnEnter
	{
		get => _sendOnEnter;
		private set
		{
			_sendOnEnter = value;
			_appSettingsFeature.SendOnEnter = value;
		}
	}

	bool _textToSpeechEnabled;
	public bool TextToSpeechEnabled
	{
		get => _textToSpeechEnabled;
		private set
		{
			_textToSpeechEnabled = value;
			_appSettingsFeature.TextToSpeechEnabled = value;
		}
	}

	public void ToggleLeftSidebar()
	{
		LeftSidebarCollapsed = !LeftSidebarCollapsed;
		OnStateChanged?.Invoke();
	}

	public void ToggleRightSidebar()
	{
		RightSidebarCollapsed = !RightSidebarCollapsed;
		OnStateChanged?.Invoke();
	}

	public void SetLeftSidebarWidth(int width)
	{
		LeftSidebarWidth = Math.Clamp(width, 150, 600);
		OnStateChanged?.Invoke();
	}

	public void SetRightSidebarWidth(int width)
	{
		RightSidebarWidth = Math.Clamp(width, 150, 600);
		OnStateChanged?.Invoke();
	}

	public void ToggleSettingsPopup()
	{
		SettingsPopupOpen = !SettingsPopupOpen;
		OnStateChanged?.Invoke();
	}

	public void OpenSettingsPopup()
	{
		if(!SettingsPopupOpen)
		{
			SettingsPopupOpen = true;
			OnStateChanged?.Invoke();
		}
	}

	public void OpenSettingsToSection(SettingsSectionEnum section)
	{
		PendingSettingsSection = section;
		if(!SettingsPopupOpen)
		{
			SettingsPopupOpen = true;
		}
		OnStateChanged?.Invoke();
	}

	public void ClearPendingSettingsSection()
	{
		PendingSettingsSection = null;
	}

	public void CloseSettingsPopup()
	{
		if(SettingsPopupOpen)
		{
			SettingsPopupOpen = false;
			OnStateChanged?.Invoke();
		}
	}

	public void ToggleRecording()
	{
		IsRecording = !IsRecording;
		OnStateChanged?.Invoke();
	}

	public void SetEnterToSend(bool value)
	{
		SendOnEnter = value;
		OnStateChanged?.Invoke();
	}

	public void SetTextToSpeechEnabled(bool value)
	{
		TextToSpeechEnabled = value;
		if(!value)
		{
			_ = _textToSpeechFeature.Stop();
		}

		OnStateChanged?.Invoke();
	}

	public event Action<string>? OnAppendChatInput;

	public void AppendChatInput(string text)
	{
		OnAppendChatInput?.Invoke(text);
	}
}
