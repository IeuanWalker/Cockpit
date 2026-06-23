using Cockpit.Features.AppSettings;
using Cockpit.Features.TextToSpeech;

namespace Cockpit.Features.UIState;

/// <inheritdoc />
public sealed class UIStateFeature : IUIStateFeature
{
	/// <summary>Minimum sidebar width in pixels.</summary>
	public const int MinSidebarWidth = 150;

	/// <summary>Maximum sidebar width in pixels.</summary>
	public const int MaxSidebarWidth = 600;

	readonly ITextToSpeechFeature _textToSpeechFeature;
	readonly IAppSettingsFeature _appSettingsFeature;

	public UIStateFeature(ITextToSpeechFeature textToSpeechFeature, IAppSettingsFeature appSettingsFeature)
	{
		_textToSpeechFeature = textToSpeechFeature;
		_appSettingsFeature = appSettingsFeature;

		LeftSidebarWidth = Math.Clamp(appSettingsFeature.LeftSidebarWidth, MinSidebarWidth, MaxSidebarWidth);
		RightSidebarWidth = Math.Clamp(appSettingsFeature.RightSidebarWidth, MinSidebarWidth, MaxSidebarWidth);
		_sendOnEnter = appSettingsFeature.SendOnEnter;
		_textToSpeechEnabled = appSettingsFeature.TextToSpeechEnabled;
	}

	/// <inheritdoc />
	public event Action? OnStateChanged;

	/// <inheritdoc />
	public event Action<string>? OnAppendChatInput;

	/// <inheritdoc />
	public bool LeftSidebarCollapsed { get; private set; } = false;

	/// <inheritdoc />
	public bool RightSidebarCollapsed { get; private set; } = false;

	int _leftSidebarWidth;

	/// <inheritdoc />
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

	/// <inheritdoc />
	public int RightSidebarWidth
	{
		get => _rightSidebarWidth;
		private set
		{
			_rightSidebarWidth = value;
			_appSettingsFeature.RightSidebarWidth = value;
		}
	}

	bool _sendOnEnter;

	/// <inheritdoc />
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

	/// <inheritdoc />
	public bool TextToSpeechEnabled
	{
		get => _textToSpeechEnabled;
		private set
		{
			_textToSpeechEnabled = value;
			_appSettingsFeature.TextToSpeechEnabled = value;
		}
	}

	/// <inheritdoc />
	public void ToggleLeftSidebar()
	{
		LeftSidebarCollapsed = !LeftSidebarCollapsed;
		OnStateChanged?.Invoke();
	}

	/// <inheritdoc />
	public void ToggleRightSidebar()
	{
		RightSidebarCollapsed = !RightSidebarCollapsed;
		OnStateChanged?.Invoke();
	}

	/// <inheritdoc />
	public void SetLeftSidebarWidth(int width)
	{
		int clamped = Math.Clamp(width, MinSidebarWidth, MaxSidebarWidth);
		if(clamped == _leftSidebarWidth)
		{
			return;
		}

		LeftSidebarWidth = clamped;
		OnStateChanged?.Invoke();
	}

	/// <inheritdoc />
	public void SetRightSidebarWidth(int width)
	{
		int clamped = Math.Clamp(width, MinSidebarWidth, MaxSidebarWidth);
		if(clamped == _rightSidebarWidth)
		{
			return;
		}

		RightSidebarWidth = clamped;
		OnStateChanged?.Invoke();
	}

	/// <inheritdoc />
	public void SetEnterToSend(bool value)
	{
		if(value == _sendOnEnter)
		{
			return;
		}

		SendOnEnter = value;
		OnStateChanged?.Invoke();
	}

	/// <inheritdoc />
	public void SetTextToSpeechEnabled(bool value)
	{
		if(value == _textToSpeechEnabled)
		{
			return;
		}

		TextToSpeechEnabled = value;
		if(!value)
		{
			_ = _textToSpeechFeature.Stop();
		}

		OnStateChanged?.Invoke();
	}

	/// <inheritdoc />
	public void AppendChatInput(string text)
	{
		OnAppendChatInput?.Invoke(text);
	}
}
