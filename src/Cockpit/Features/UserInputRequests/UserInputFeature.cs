using System.Collections.Concurrent;
using System.Text.Json;
using Cockpit.Features.Sessions;
using Cockpit.Features.Sessions.Models;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging;

namespace Cockpit.Features.UserInputRequests;

/// <summary>
/// Service for managing user input requests from Copilot SDK
/// </summary>
public sealed class UserInputFeature : IUserInputHandler
{
	readonly ISessionStateProvider _sessionStateProvider;
	readonly ILogger<UserInputFeature> _logger;

	// In-memory cache of pending requests
	readonly ConcurrentDictionary<string, UserInputRequestModel> _pendingRequests = new(); // Key: request.Id

	// Events for UI updates
	public event Action<string, UserInputRequestModel>? OnUserInputRequested;
	public event Action<string, string>? OnUserInputResolved;

	public UserInputFeature(ISessionStateProvider sessionStateProvider, ILogger<UserInputFeature> logger)
	{
		_sessionStateProvider = sessionStateProvider;
		_logger = logger;
	}

	UserInputRequestModel ToRequestModel(UserInputRequest request, string sessionId)
	{
		List<string> choices = request.Choices is not null
			? [.. request.Choices.Where(c => !string.IsNullOrEmpty(c))]
			: [];

		return new UserInputRequestModel
		{
			SessionId = sessionId,
			Question = request.Question,
			AllowsTextInput = request.AllowFreeform ?? true,
			Choices = choices,
			FullRequestJson = JsonSerializer.Serialize(request)
		};
	}

	public async Task<UserInputResponse> HandleUserInputRequest(UserInputRequest request, UserInputInvocation invocation)
	{
		try
		{
			SessionModel? session = _sessionStateProvider.Sessions.FirstOrDefault(s => s.Id == invocation.SessionId);
			if(session is null)
			{
				_logger.LogWarning("SessionModel not found for SDK session {SessionId}", invocation.SessionId);
				return new UserInputResponse();
			}

			UserInputRequestModel userInputRequest = ToRequestModel(request, invocation.SessionId);

			_logger.LogInformation("User input request: Question='{Question}', SessionId={SessionId}", userInputRequest.Question, session.Id);

			// Request user response
			string? response = await RequestUserResponseAsync(userInputRequest);

			_logger.LogInformation("User input response: {Response}", response ?? "(cancelled)");

			bool isChoice = userInputRequest.Choices.Contains(response ?? string.Empty);
			return new UserInputResponse
			{
				Answer = response ?? string.Empty,
				WasFreeform = !isChoice
			};
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Error in user input handler");
			return new UserInputResponse();
		}
	}

	async Task<string?> RequestUserResponseAsync(UserInputRequestModel request)
	{
		// Store pending request using unique request ID
		_pendingRequests[request.Id] = request;

		// Notify UI
		UpdateSessionOnUserInputRequested(request.SessionId, request);
		OnUserInputRequested?.Invoke(request.SessionId, request);

		// Wait for user response
		try
		{
			string? response = await request.GetResponseAsync();
			return response;
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Error waiting for user input response");
			return null;
		}
		finally
		{
			// Clean up pending request using request ID
			_pendingRequests.TryRemove(request.Id, out _);
		}
	}

	void UpdateSessionOnUserInputRequested(string sessionId, UserInputRequestModel request)
	{
		SessionModel? session = _sessionStateProvider.Sessions.FirstOrDefault(s => s.Id == sessionId);
		if(session is null)
		{
			return;
		}

		_logger.LogInformation("User input requested - Adding request ID: {RequestId} to session {SessionId}", request.Id, sessionId);

		bool setStatus;
		lock(session.UserInputRequestsLock)
		{
			if(!session.PendingUserInputRequests.TryAdd(request.Id, request))
			{
				_logger.LogWarning("User input request {RequestId} already exists for session {SessionId}", request.Id, sessionId);
				return;
			}

			setStatus = session.PendingUserInputRequests.Count == 1;
		}

		if(setStatus)
		{
			lock(session.StatusHistoryLock)
			{
				// Only save to history when transitioning from a non-blocking status
				if(session.Status is not SessionStatusEnum.NeedsPermission and not SessionStatusEnum.NeedsUserInput)
				{
					session.StatusHistory.Push(session.Status);
				}
				session.Status = SessionStatusEnum.NeedsUserInput;
			}
		}

		_sessionStateProvider.NotifyStateChanged();
	}

	void UpdateSessionOnUserInputResolved(string sessionId, string requestId)
	{
		SessionModel? session = _sessionStateProvider.Sessions.FirstOrDefault(s => s.Id == sessionId);
		if(session is null)
		{
			return;
		}

		_logger.LogInformation("User input resolved - Removing request ID: {RequestId} from session {SessionId}", requestId, sessionId);

		bool restoreStatus;
		lock(session.UserInputRequestsLock)
		{
			session.PendingUserInputRequests.TryRemove(requestId, out _);
			restoreStatus = session.PendingUserInputRequests.IsEmpty;
		}

		if(restoreStatus)
		{
			lock(session.StatusHistoryLock)
			{
				// If permission requests are still pending, switch to that status rather than restoring base
				if(session.PendingPermissionRequests.Count > 0)
				{
					session.Status = SessionStatusEnum.NeedsPermission;
				}
				else
				{
					session.Status = session.StatusHistory.TryPop(out SessionStatusEnum prev) ? prev : SessionStatusEnum.Idle;
				}
			}
		}

		_sessionStateProvider.NotifyStateChanged();
	}

	/// <summary>
	/// Simulate a user input request for debugging/testing.
	/// </summary>
	public Task<string?> SimulateTextRequestAsync(string sessionId)
	{
		UserInputRequestModel request = new()
		{
			SessionId = sessionId,
			Question = "What would you like the agent to focus on?",
			Placeholder = "e.g. improve performance, add tests...",
			AllowsTextInput = true,
			Choices = ["Improve performance", "Add tests", "Refactor code"],
			IsRequired = false,
			FullRequestJson = "{\"debug\":true}"
		};
		return RequestUserResponseAsync(request);
	}

	/// <summary>
	/// Simulate a user input request with choices only (no text field) for debugging/testing.
	/// </summary>
	public Task<string?> SimulateChoicesRequestAsync(string sessionId)
	{
		UserInputRequestModel request = new()
		{
			SessionId = sessionId,
			Question = "Which approach should I use to handle the error?",
			AllowsTextInput = false,
			Choices = ["Throw an exception", "Return null", "Log and continue", "Retry automatically"],
			IsRequired = true,
			FullRequestJson = "{\"debug\":true}"
		};
		return RequestUserResponseAsync(request);
	}

	/// <summary>
	/// Resolve a pending user input request with user response
	/// </summary>
	public void ResolveUserInputRequest(string requestId, string? response)
	{
		_logger.LogInformation("ResolveUserInputRequest called with requestId: {RequestId}, response: {Response}", requestId, response ?? "(cancelled)");

		if(!_pendingRequests.TryGetValue(requestId, out UserInputRequestModel? request))
		{
			_logger.LogWarning("No pending user input request found for request ID {RequestId}. Current pending count: {Count}", requestId, _pendingRequests.Count);
			return;
		}

		_logger.LogInformation(
			"User input {Status} for session {SessionId}: {Question}",
			response is not null ? "provided" : "cancelled",
			request.SessionId,
			request.Question);

		// Complete the TaskCompletionSource
		bool completed = request.CompletionSource.TrySetResult(response);
		_logger.LogDebug("TaskCompletionSource completed: {Completed}", completed);

		// Notify UI with requestId so it can be removed from session list
		UpdateSessionOnUserInputResolved(request.SessionId, request.Id);
		OnUserInputResolved?.Invoke(request.SessionId, request.Id);
	}
}
