namespace Cockpit.Features.UIState;

/// <summary>
/// Manages shared visual state for the application UI, including sidebar
/// collapse states, panel widths, input behaviour, and cross-component signals.
/// </summary>
public interface IUIStateFeature
{
	/// <summary>Raised whenever any observable state property changes.</summary>
	event Action? OnStateChanged;

	/// <summary>Raised when text should be appended to the active chat input.</summary>
	event Action<string>? OnAppendChatInput;

	/// <summary>Whether the left sidebar is currently collapsed.</summary>
	bool LeftSidebarCollapsed { get; }

	/// <summary>Whether the right sidebar is currently collapsed.</summary>
	bool RightSidebarCollapsed { get; }

	/// <summary>Current width of the left sidebar in pixels.</summary>
	int LeftSidebarWidth { get; }

	/// <summary>Current width of the right sidebar in pixels.</summary>
	int RightSidebarWidth { get; }

	/// <summary>Whether pressing Enter sends the current message.</summary>
	bool SendOnEnter { get; }

	/// <summary>Whether text-to-speech output is enabled.</summary>
	bool TextToSpeechEnabled { get; }

	/// <summary>Toggles the left sidebar between collapsed and expanded.</summary>
	void ToggleLeftSidebar();

	/// <summary>Toggles the right sidebar between collapsed and expanded.</summary>
	void ToggleRightSidebar();

	/// <summary>
	/// Sets the left sidebar width, clamped to [<see cref="UIStateFeature.MinSidebarWidth"/>, <see cref="UIStateFeature.MaxSidebarWidth"/>].
	/// No event is raised when the clamped value equals the current width.
	/// </summary>
	void SetLeftSidebarWidth(int width);

	/// <summary>
	/// Sets the right sidebar width, clamped to [<see cref="UIStateFeature.MinSidebarWidth"/>, <see cref="UIStateFeature.MaxSidebarWidth"/>].
	/// No event is raised when the clamped value equals the current width.
	/// </summary>
	void SetRightSidebarWidth(int width);

	/// <summary>
	/// Sets whether pressing Enter sends the message.
	/// No event is raised when the value is unchanged.
	/// </summary>
	void SetEnterToSend(bool value);

	/// <summary>
	/// Enables or disables text-to-speech. When disabled, stops any active speech immediately.
	/// No event is raised when the value is unchanged.
	/// </summary>
	void SetTextToSpeechEnabled(bool value);

	/// <summary>
	/// Appends <paramref name="text"/> to the active chat input by raising
	/// <see cref="OnAppendChatInput"/>.
	/// </summary>
	void AppendChatInput(string text);
}
