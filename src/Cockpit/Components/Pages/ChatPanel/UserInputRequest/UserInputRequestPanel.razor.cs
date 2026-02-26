using Cockpit.Features.AppSettings;
using Cockpit.Features.Sessions;
using Cockpit.Features.UserInputRequests;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Cockpit.Components.Pages.ChatPanel.UserInputRequest;

public sealed partial class UserInputRequestPanel : ComponentBase, IDisposable
{
	readonly IAppSettingsFeature _appSettingsFeature;
	readonly SessionListFeature _sessionListFeature;
	readonly UserInputFeature _userInputFeature;
	readonly IJSRuntime _jsRuntime;
	readonly ILogger<UserInputRequestPanel> _logger;

	public UserInputRequestPanel(
		IAppSettingsFeature appSettingsFeature,
		SessionListFeature sessionListFeature,
		UserInputFeature userInputFeature,
		IJSRuntime jsRuntime,
		ILogger<UserInputRequestPanel> logger)
	{
		_appSettingsFeature = appSettingsFeature;
		_sessionListFeature = sessionListFeature;
		_userInputFeature = userInputFeature;
		_jsRuntime = jsRuntime;
		_logger = logger;
	}

	protected override void OnInitialized()
	{
		_sessionListFeature.OnStateChanged += OnStateChanged;
	}

	public string UserTextInput
	{
		get => _sessionListFeature.CurrentSession?.UserInputResponseText ?? string.Empty;
		set => _sessionListFeature.CurrentSession?.UserInputResponseText = value;
	}

	public UserInputRequestModel? Request => _sessionListFeature.CurrentSession?.PendingUserInputRequests?.Values.OrderBy(r => r.Requested).FirstOrDefault();
	bool CanSubmitText => !string.IsNullOrWhiteSpace(UserTextInput);

	UserInputRequestDetailsPopup _detailsPopup = default!;

	bool ShowInfoPopup { get; set; }

	void ShowRequestInfo() => _detailsPopup.Open();

	void HideRequestInfo()
	{
		ShowInfoPopup = false;
	}

	void OnStateChanged()
	{
		InvokeAsync(async () =>
		{
			StateHasChanged();
			await Task.Delay(10);
			await ResizeTextarea();
		});
	}

	public Task OnSubmit()
	{
		UserInputRequestModel? currentRequest = Request;
		if(currentRequest is null)
		{
			_logger.LogWarning("OnSubmit called but no pending user input request was found");
			return Task.CompletedTask;
		}

		string response = UserTextInput.Trim();
		if(string.IsNullOrWhiteSpace(response))
		{
			_logger.LogWarning("Text submit ignored because input was empty");
			return Task.CompletedTask;
		}

		_logger.LogInformation("OnSubmit called: sessionId={SessionId}, responseLength={Length}", currentRequest.SessionId, response.Length);

		_userInputFeature.ResolveUserInputRequest(currentRequest.Id, response);
		ClearCurrentSessionInputState();
		return Task.CompletedTask;
	}

	void OnCancel()
	{
		UserInputRequestModel? currentRequest = Request;
		if(currentRequest is null)
		{
			_logger.LogWarning("OnCancel called but no pending user input request was found");
			return;
		}

		_logger.LogInformation("OnCancel called: sessionId={SessionId}", currentRequest.SessionId);

		_userInputFeature.ResolveUserInputRequest(currentRequest.Id, null);
		ClearCurrentSessionInputState();
	}

	void OnChoiceSelected(string choice)
	{
		UserInputRequestModel? currentRequest = Request;
		if(currentRequest is null)
		{
			_logger.LogWarning("OnChoiceSelected called but no pending user input request was found");
			return;
		}

		_logger.LogInformation("Choice selected: choice={Choice}, sessionId={SessionId}", choice, currentRequest.SessionId);
		_userInputFeature.ResolveUserInputRequest(currentRequest.Id, choice);
		ClearCurrentSessionInputState();
	}

	async Task OnKeyDown(Microsoft.AspNetCore.Components.Web.KeyboardEventArgs e)
	{
		if(e.Key == "Enter" && !e.ShiftKey && _appSettingsFeature.SendOnEnter && CanSubmitText)
		{
			await OnSubmit();
			await ResizeTextarea();
		}
	}

	async Task ResizeTextarea()
	{
		try
		{
			await _jsRuntime.InvokeVoidAsync("cockpit.autoResizeTextarea", "userInputRequestField");
		}
		catch { /* ignore if JS unavailable */ }
	}

	void ClearCurrentSessionInputState()
	{
		if(_sessionListFeature.CurrentSession is null)
		{
			return;
		}

		_sessionListFeature.CurrentSession.UserInputResponseText = string.Empty;
	}

	public void Dispose()
	{
		_sessionListFeature.OnStateChanged -= OnStateChanged;
	}
}
