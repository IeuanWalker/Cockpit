using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace CopilotGUI.Components;

public partial class WorkingDirectoryDialog : ComponentBase, IDisposable
{
	[Inject] IJSRuntime JSRuntime { get; set; } = default!;

	[Parameter] public EventCallback<string?> OnDirectorySelected { get; set; }

	bool _isOpen = false;
	string _selectedPath = string.Empty;
	string? _errorMessage;
	string? _userHomeDirectory;
	string? _documentsDirectory;
	List<string> _recentDirectories = new();

	protected override void OnInitialized()
	{
		_userHomeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		_documentsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

		// Load recent directories from localStorage (to be implemented)
		LoadRecentDirectories();
	}

	public void Open(string? defaultPath = null)
	{
		_selectedPath = defaultPath ?? _userHomeDirectory ?? string.Empty;
		_errorMessage = null;
		_isOpen = true;
		StateHasChanged();
	}

	public void Close()
	{
		_isOpen = false;
		StateHasChanged();
	}

	void SelectPath(string path)
	{
		_selectedPath = path;
		_errorMessage = null;
		StateHasChanged();
	}

	async Task BrowseDirectory()
	{
		try
		{
#if WINDOWS
			var folderPicker = new Windows.Storage.Pickers.FolderPicker();
			
			// Get the current window handle properly in MAUI
			var window = Microsoft.Maui.Controls.Application.Current?.Windows[0]?.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
			if (window != null)
			{
				var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
				WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hwnd);
			}
			
			folderPicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
			folderPicker.FileTypeFilter.Add("*");

			var folder = await folderPicker.PickSingleFolderAsync();
			if (folder != null)
			{
				_selectedPath = folder.Path;
				_errorMessage = null;
			}
#elif MACCATALYST
			// macOS directory picker
			var picker = new UIKit.UIDocumentPickerViewController(
				new[] { UniformTypeIdentifiers.UTTypes.Folder },
				false);
			
			var tcs = new TaskCompletionSource<string?>();
			
			picker.DidPickDocument += (sender, e) =>
			{
				tcs.SetResult(e.Url?.Path);
			};
			
			picker.WasCancelled += (sender, e) =>
			{
				tcs.SetResult(null);
			};

			var viewController = Platform.GetCurrentUIViewController();
			if (viewController != null)
			{
				await viewController.PresentViewControllerAsync(picker, true);
				
				var path = await tcs.Task;
				if (!string.IsNullOrEmpty(path))
				{
					_selectedPath = path;
					_errorMessage = null;
				}
			}
#endif
			StateHasChanged();
		}
		catch (Exception ex)
		{
			_errorMessage = $"Failed to open directory picker: {ex.Message}";
			StateHasChanged();
		}
	}

	bool IsValidDirectory()
	{
		if (string.IsNullOrWhiteSpace(_selectedPath))
			return false;

		try
		{
			return Directory.Exists(_selectedPath);
		}
		catch
		{
			return false;
		}
	}

	async Task Confirm()
	{
		if (!IsValidDirectory())
		{
			_errorMessage = "Please select a valid directory";
			return;
		}

		// Save to recent directories
		SaveToRecentDirectories(_selectedPath);

		await OnDirectorySelected.InvokeAsync(_selectedPath);
		Close();
	}

	void Cancel()
	{
		_selectedPath = string.Empty;
		_errorMessage = null;
		Close();
	}

	void LoadRecentDirectories()
	{
		// TODO: Load from localStorage or settings
		// For now, just add the current directory
		var currentDir = Directory.GetCurrentDirectory();
		if (Directory.Exists(currentDir))
		{
			_recentDirectories.Add(currentDir);
		}
	}

	void SaveToRecentDirectories(string path)
	{
		if (!_recentDirectories.Contains(path))
		{
			_recentDirectories.Insert(0, path);
			if (_recentDirectories.Count > 5)
			{
				_recentDirectories.RemoveAt(_recentDirectories.Count - 1);
			}
			// TODO: Save to localStorage
		}
	}

	public void Dispose()
	{
		Dispose(true);
		GC.SuppressFinalize(this);
	}

	protected virtual void Dispose(bool disposing)
	{
		// Cleanup if needed
	}
}
