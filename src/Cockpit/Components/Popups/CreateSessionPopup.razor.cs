using Cockpit.Components.Controls;
using Cockpit.Features.Sessions;
using CommunityToolkit.Maui.Storage;
using Microsoft.AspNetCore.Components;


namespace Cockpit.Components.Popups;

public partial class CreateSessionPopup : ComponentBase
{
	readonly SessionFeature _sessionFeature;

	public CreateSessionPopup(SessionFeature sessionFeature)
	{
		_sessionFeature = sessionFeature;
	}

	public string SelectedPath { get; set; } = string.Empty;
	public string? ErrorMessage { get; set; } = string.Empty;
	public List<string> RecentDirectories { get; set; } = [];
	PopupBase _popup = default!;

	public void Open()
	{
		SelectedPath = string.Empty;
		ErrorMessage = null;
		RecentDirectories = [.. _sessionFeature.Sessions
			.OrderByDescending(s => s.LastActivity)
			.Where(x => !string.IsNullOrWhiteSpace(x.Context.CurrentWorkingDirectory))
			.Select(s => s.Context.CurrentWorkingDirectory)
			.Distinct()
			.Take(5)];

		_popup.Open();

		StateHasChanged();
	}

	void SelectPath(string path)
	{
		SelectedPath = path;
		ErrorMessage = null;
		StateHasChanged();
	}

	async Task BrowseDirectory()
	{
		try
		{
			FolderPickerResult result = await FolderPicker.Default.PickAsync();
			if(result.IsSuccessful)
			{
				SelectedPath = result.Folder.Path;
				ErrorMessage = null;
			}
			else
			{
				if(result.Exception is not OperationCanceledException)
				{
					ErrorMessage = $"Failed to open directory picker: {result.Exception.Message}";
				}
			}

			StateHasChanged();
		}
		catch(Exception ex)
		{
			ErrorMessage = $"Failed to open directory picker: {ex.Message}";
			StateHasChanged();
		}
	}

	bool IsValidDirectory()
	{
		if(string.IsNullOrWhiteSpace(SelectedPath))
		{
			return false;
		}

		try
		{
			return Directory.Exists(SelectedPath);
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
			ErrorMessage = "Please select a valid directory";
			return;
		}

		try
		{
			await _sessionFeature.CreateSession(SelectedPath);
			_popup.Close();

		}
		catch(Exception ex)
		{
			ErrorMessage = $"Error creating session: {ex.Message}";
		}
	}

	void Cancel() => _popup.Close();
}
