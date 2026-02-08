namespace CopilotGUI.Services;

public class UIStateService
{
	public event Action? OnStateChanged;

	// Sidebar states
	public bool LeftSidebarCollapsed { get; private set; } = false;
	public bool RightSidebarCollapsed { get; private set; } = false;
	public int LeftSidebarWidth { get; private set; } = 224; // 14rem = 224px
	public int RightSidebarWidth { get; private set; } = 256; // 16rem = 256px

	// Settings popup
	public bool SettingsPopupOpen { get; private set; } = false;

	// Microphone recording
	public bool IsRecording { get; private set; } = false;

	// Input behavior
	public bool EnterToSend { get; private set; } = true;

	// Dropdown states
	readonly Dictionary<string, bool> _dropdownStates = [];

	public void ToggleLeftSidebar()
	{
		LeftSidebarCollapsed = !LeftSidebarCollapsed;
		NotifyStateChanged();
	}

	public void ToggleRightSidebar()
	{
		RightSidebarCollapsed = !RightSidebarCollapsed;
		NotifyStateChanged();
	}

	public void SetLeftSidebarWidth(int width)
	{
		LeftSidebarWidth = Math.Clamp(width, 150, 600);
		NotifyStateChanged();
	}

	public void SetRightSidebarWidth(int width)
	{
		RightSidebarWidth = Math.Clamp(width, 150, 600);
		NotifyStateChanged();
	}

	public void ToggleSettingsPopup()
	{
		SettingsPopupOpen = !SettingsPopupOpen;
		NotifyStateChanged();
	}

	public void CloseSettingsPopup()
	{
		if(SettingsPopupOpen)
		{
			SettingsPopupOpen = false;
			NotifyStateChanged();
		}
	}

	public void ToggleRecording()
	{
		IsRecording = !IsRecording;
		NotifyStateChanged();
	}

	public void SetEnterToSend(bool value)
	{
		EnterToSend = value;
		NotifyStateChanged();
	}

	public void ToggleDropdown(string dropdownId)
	{
		_dropdownStates[dropdownId] = !_dropdownStates.TryGetValue(dropdownId, out bool value) || (_dropdownStates[dropdownId] = !value);
		NotifyStateChanged();
	}

	public bool IsDropdownOpen(string dropdownId)
	{
		return _dropdownStates.TryGetValue(dropdownId, out bool isOpen) && isOpen;
	}

	public void SetDropdownOpen(string dropdownId, bool isOpen)
	{
		_dropdownStates[dropdownId] = isOpen;
		NotifyStateChanged();
	}

	void NotifyStateChanged() => OnStateChanged?.Invoke();
}
