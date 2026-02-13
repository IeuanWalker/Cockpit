namespace Cockpit.Services;

public class UIStateService
{
	public event Action? OnStateChanged;

	public bool LeftSidebarCollapsed { get; private set; } = false;
	public bool RightSidebarCollapsed { get; private set; } = false;

	int _leftSidebarWidth = UserAppSettings.LeftSidebarWidth;
	public int LeftSidebarWidth
	{
		get => _leftSidebarWidth;
		private set
		{
			_leftSidebarWidth = value;
			UserAppSettings.LeftSidebarWidth = value;
		}
	}

	int _rightSidebarWidth = UserAppSettings.RightSidebarWidth;
	public int RightSidebarWidth
	{
		get => _rightSidebarWidth;
		private set
		{
			_rightSidebarWidth = value;
			UserAppSettings.RightSidebarWidth = value;
		}
	}

	public bool SettingsPopupOpen { get; private set; } = false;

	public bool IsRecording { get; private set; } = false;

	bool _sendOnEnter = UserAppSettings.SendOnEnter;
	public bool SendOnEnter
	{
		get => _sendOnEnter;
		private set
		{
			_sendOnEnter = value;
			UserAppSettings.SendOnEnter = value;
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

	readonly Dictionary<string, bool> _dropdownStates = [];

	public void ToggleDropdown(string dropdownId)
	{
		_dropdownStates[dropdownId] = !_dropdownStates.TryGetValue(dropdownId, out bool value) || (_dropdownStates[dropdownId] = !value);
		OnStateChanged?.Invoke();
	}

	public bool IsDropdownOpen(string dropdownId)
	{
		return _dropdownStates.TryGetValue(dropdownId, out bool isOpen) && isOpen;
	}

	public void SetDropdownOpen(string dropdownId, bool isOpen)
	{
		_dropdownStates[dropdownId] = isOpen;
		OnStateChanged?.Invoke();
	}
}
