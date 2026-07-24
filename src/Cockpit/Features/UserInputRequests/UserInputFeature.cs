using System.Collections.Concurrent;
using Cockpit.Extensions;
using Cockpit.Features.Sessions;
using Cockpit.Features.Sessions.Interactions;
using Cockpit.Features.Sessions.Models;
using GitHub.Copilot;
using Microsoft.Extensions.Logging;

namespace Cockpit.Features.UserInputRequests;

/// <summary>
/// Service for managing user input requests from Copilot SDK
/// </summary>
public sealed class UserInputFeature : IUserInputHandler, IUserInputEventSource
{
	readonly ISessionStateProvider _sessionStateProvider;
	readonly SessionInteractionCoordinator _interactionCoordinator;
	readonly ILogger<UserInputFeature> _logger;

	// In-memory cache of pending requests
	readonly ConcurrentDictionary<string, UserInputRequestModel> _pendingRequests = new(); // Key: request.Id

	// Events for UI updates
	public event Action<string, UserInputRequestModel>? OnUserInputRequested;
	public event Action<string, string>? OnUserInputResolved;

	public UserInputFeature(
		ISessionStateProvider sessionStateProvider,
		ILogger<UserInputFeature> logger,
		SessionInteractionCoordinator? interactionCoordinator = null)
	{
		_sessionStateProvider = sessionStateProvider;
		_interactionCoordinator = interactionCoordinator ?? new SessionInteractionCoordinator(sessionStateProvider);
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
			FullRequestJson = request.SerializeJson() ?? string.Empty
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

			_logger.LogDebug("User input response received (length={Length})", response?.Length ?? 0);

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
		_pendingRequests[request.Id] = request;

		try
		{
			// Notify UI — inside try so cleanup always runs even if a subscriber throws
			_interactionCoordinator.AddUserInput(request.SessionId, request);

			try
			{
				OnUserInputRequested?.Invoke(request.SessionId, request);
			}
			catch(Exception ex)
			{
				_logger.LogError(ex, "Subscriber exception in OnUserInputRequested for request {RequestId}", request.Id);
			}

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
			_pendingRequests.TryRemove(request.Id, out _);
		}
	}

	/// <summary>
	/// Simulate a user input request for debugging/testing.
	/// </summary>
	public Task<string?> SimulateTextRequest(string sessionId)
	{
		UserInputRequestModel request = new()
		{
			SessionId = sessionId,
			Question = "What would you like the agent to focus on?",
			AllowsTextInput = true,
			FullRequestJson = "{\"debug\":true}"
		};
		return RequestUserResponseAsync(request);
	}

	/// <summary>
	/// Simulate a user input request with choices only (no text field) for debugging/testing.
	/// </summary>
	public Task<string?> SimulateChoicesRequest(string sessionId)
	{
		UserInputRequestModel request = new()
		{
			SessionId = sessionId,
			Question = "Which approach should I use to handle the error?",
			AllowsTextInput = false,
			Choices = ["Throw an exception", "Return null", "Log and continue", "Retry automatically"],
			FullRequestJson = "{\"debug\":true}"
		};
		return RequestUserResponseAsync(request);
	}

	/// <summary>
	/// Simulate a user input request with both text and choices for debugging/testing.
	/// </summary>
	public Task<string?> SimulateTextAndChoicesRequest(string sessionId)
	{
		UserInputRequestModel request = new()
		{
			SessionId = sessionId,
			Question = "Which approach should I use to handle the error?",
			AllowsTextInput = true,
			Choices = ["Throw an exception", "Return null", "Log and continue", "Retry automatically"],
			FullRequestJson = "{\"debug\":true}"
		};
		return RequestUserResponseAsync(request);
	}

	/// <summary>
	/// Resolve a pending user input request with user response
	/// </summary>
	public void ResolveUserInputRequest(string requestId, string? response)
	{
		_logger.LogInformation("ResolveUserInputRequest called with requestId: {RequestId}, was cancelled: {Cancelled}", requestId, response is null);

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

		if(!completed)
		{
			_logger.LogWarning("ResolveUserInputRequest: request {RequestId} was already resolved (concurrent call?)", requestId);
			return;
		}

		_interactionCoordinator.ResolveUserInput(request.SessionId, request.Id);

		try
		{
			OnUserInputResolved?.Invoke(request.SessionId, request.Id);
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Subscriber exception in OnUserInputResolved for request {RequestId}", request.Id);
		}
	}

	/// <summary>
	/// Cancels all pending user input requests for a session (e.g., when the session is deleted).
	/// </summary>
	public void CancelPendingRequestsForSession(string sessionId)
	{
		List<string> requestIds = [.. _pendingRequests.Values
			.Where(r => r.SessionId == sessionId)
			.Select(r => r.Id)];

		foreach(string requestId in requestIds)
		{
			_logger.LogInformation("Cancelling pending user input request {RequestId} for deleted session {SessionId}", requestId, sessionId);
			ResolveUserInputRequest(requestId, null);
		}
	}
}
