using Blazor.Sonner.Services;
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
	readonly ToastService _toastService;
	public CreateSessionPopup(SessionFeature sessionFeature, ILogger<CreateSessionPopup> logger, ToastService toastService)
	{
		_sessionFeature = sessionFeature;
		_logger = logger;
		_toastService = toastService;
	}

	public string ErrorMessage { get; set; } = string.Empty;
	public List<string> RecentDirectories { get; set; } = [];
	bool _isCreating;
	CancellationTokenSource? _createSessionCts;
	PopupBase _popup = default!;

	public void Open()
	{
		ErrorMessage = string.Empty;
		RecentDirectories = [.. _sessionFeature.Sessions
			.OrderByDescending(s => s.LastActivity)
			.Where(x => !string.IsNullOrWhiteSpace(x.Context.CurrentWorkingDirectory))
			.Select(s => s.Context.CurrentWorkingDirectory!)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.Take(5)];

		_popup.Open();

		StateHasChanged();
	}

	Task CreateQuickChat() => CreateSession(null);

	Task CreateFromRecent(string path) => CreateSession(path);

	async Task BrowseDirectory()
	{
#if WINDOWS || MACCATALYST
		try
		{
			FolderPickerResult result = await FolderPicker.Default.PickAsync();
			if(result.IsSuccessful)
			{
				await CreateSession(result.Folder.Path);
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
		catch(OperationCanceledException)
		{
			// User cancelled the picker — no error to show
		}
		catch(Exception ex)
		{
			ErrorMessage = $"Failed to open directory picker: {ex.Message}";
			_logger.LogError(ex, "Failed to open directory picker");
			StateHasChanged();
		}
#endif
	}

	bool IsValidDirectory(string? directoryPath)
	{
		if(string.IsNullOrWhiteSpace(directoryPath))
		{
			return false;
		}

		try
		{
			return Directory.Exists(directoryPath);
		}
		catch
		{
			return false;
		}
	}

	async Task CreateSession(string? workingDirectory)
	{
		if(_isCreating)
		{
			return;
		}

		if(!string.IsNullOrWhiteSpace(workingDirectory) && !IsValidDirectory(workingDirectory))
		{
			ErrorMessage = "Please select a valid directory";
			return;
		}

		_isCreating = true;
		ErrorMessage = string.Empty;
		_createSessionCts = new CancellationTokenSource();
		StateHasChanged();

		string? pathToCreate = workingDirectory;
		try
		{
			await _sessionFeature.CreateSession(pathToCreate, _createSessionCts.Token);
			_popup.Close();
		}
		catch(OperationCanceledException)
		{
			ErrorMessage = "Session creation cancelled";
		}
		catch(Exception ex)
		{
			_toastService.Error("Failed to create session", opts => opts.Description = ex.Message);
			_logger.LogError(ex, "Error creating session for directory {Directory}", pathToCreate);
		}
		finally
		{
			_createSessionCts?.Dispose();
			_createSessionCts = null;
			_isCreating = false;
			StateHasChanged();
		}
	}

	void Cancel()
	{
		_createSessionCts?.Cancel();
		_popup.Close();
	}
}
