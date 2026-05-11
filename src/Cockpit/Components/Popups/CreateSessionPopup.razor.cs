using Cockpit.Components.Controls;
using Cockpit.Features.Sessions;
using CommunityToolkit.Maui.Storage;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;


namespace Cockpit.Components.Popups;

public partial class CreateSessionPopup : ComponentBase
{
	readonly SessionFeature _sessionFeature;
	readonly ILogger<CreateSessionPopup> _logger;
	public CreateSessionPopup(SessionFeature sessionFeature, ILogger<CreateSessionPopup> logger)
	{
		_sessionFeature = sessionFeature;
		_logger = logger;
	}

	public string SelectedPath { get; set; } = string.Empty;
	public string ErrorMessage { get; set; } = string.Empty;
	public List<string> RecentDirectories { get; set; } = [];
	PopupBase _popup = default!;

	public void Open()
	{
		SelectedPath = string.Empty;
		ErrorMessage = string.Empty;
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
		ErrorMessage = string.Empty;
		StateHasChanged();
	}

	async Task BrowseDirectory()
	{
#if WINDOWS || MACCATALYST
		try
		{
			FolderPickerResult result = await FolderPicker.Default.PickAsync();
			if(result.IsSuccessful)
			{
				SelectedPath = result.Folder.Path;
				ErrorMessage = string.Empty;
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
			_logger.LogError(ex, "Failed to open directory picker");
			StateHasChanged();
		}
#endif
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
			_logger.LogError(ex, "Error creating session for directory {Directory}", SelectedPath);
		}
	}

	void Cancel() => _popup.Close();
}
