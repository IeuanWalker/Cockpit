using Cockpit.Services;
using CommunityToolkit.Maui.Storage;
using Microsoft.AspNetCore.Components;


namespace Cockpit.Components.Popups;

public partial class CreateSessionPopup : ComponentBase, IDisposable
{
	[Inject] UnifiedSessionManager SessionManager { get; set; } = default!;

	bool _isOpen = false;
	string _selectedPath = string.Empty;
	string? _errorMessage;
	readonly List<string> _recentDirectories = [];

	protected override void OnInitialized()
	{
		LoadRecentDirectories();
	}

	public void Open()
	{
		_selectedPath = string.Empty;
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
			FolderPickerResult result = await FolderPicker.Default.PickAsync();
			if(result.IsSuccessful)
			{
				_selectedPath = result.Folder.Path;
				_errorMessage = null;
			}
			else
			{
				if(result.Exception is not OperationCanceledException)
				{
					_errorMessage = $"Failed to open directory picker: {result.Exception.Message}";
				}
			}

			StateHasChanged();
		}
		catch(Exception ex)
		{
			_errorMessage = $"Failed to open directory picker: {ex.Message}";
			StateHasChanged();
		}
	}

	bool IsValidDirectory()
	{
		if(string.IsNullOrWhiteSpace(_selectedPath))
		{
			return false;
		}

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
		if(!IsValidDirectory())
		{
			_errorMessage = "Please select a valid directory";
			return;
		}

		try
		{
			await SessionManager.CreateNewSessionAsync(_selectedPath);
		}
		catch(Exception ex)
		{
			Console.Error.WriteLine($"Failed to create session: {ex.Message}");
		}

		Close();

		// Save to recent directories
		SaveToRecentDirectories(_selectedPath);
	}

	void Cancel()
	{
		_selectedPath = string.Empty;
		_errorMessage = null;
		Close();
	}

	void LoadRecentDirectories()
	{
		// TODO: Load previous sessions
	}

	void SaveToRecentDirectories(string path)
	{
		if(!_recentDirectories.Contains(path))
		{
			_recentDirectories.Insert(0, path);
			if(_recentDirectories.Count > 5)
			{
				_recentDirectories.RemoveAt(_recentDirectories.Count - 1);
			}
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
