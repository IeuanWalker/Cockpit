using System.Collections.Concurrent;
using Cockpit.Features.Sessions;
using Cockpit.Features.Sessions.Models;
using GitHub.Copilot;
using GitHub.Copilot.Rpc;
using Microsoft.Extensions.Logging;

namespace Cockpit.Features.ElicitationRequests;

/// <summary>
/// Service for managing elicitation requests from MCP servers via the Copilot SDK.
/// </summary>
public sealed partial class ElicitationFeature : IElicitationHandler, IElicitationEventSource
{
	readonly ISessionStateProvider _sessionStateProvider;
	readonly ILogger<ElicitationFeature> _logger;

	readonly ConcurrentDictionary<string, ElicitationRequestModel> _pendingRequests = new();

	public event Action<string, ElicitationRequestModel>? OnElicitationRequested;

	public ElicitationFeature(ISessionStateProvider sessionStateProvider, ILogger<ElicitationFeature> logger)
	{
		_sessionStateProvider = sessionStateProvider;
		_logger = logger;
	}

	ElicitationRequestModel ToRequestModel(ElicitationContext context)
	{
		ElicitationSchemaField[] fields = context.RequestedSchema is not null
			? ElicitationSchemaField.ParseFrom(context.RequestedSchema)
			: [];

		return new ElicitationRequestModel
		{
			SessionId = context.SessionId,
			Message = context.Message ?? string.Empty,
			Schema = context.RequestedSchema,
			Fields = fields,
			Mode = context.Mode,
			ElicitationSource = context.ElicitationSource ?? string.Empty,
			Url = context.Url,
		};
	}

	public async Task<ElicitationResult> HandleElicitationRequest(ElicitationContext context)
	{
		try
		{
			SessionModel? session = _sessionStateProvider.Sessions.FirstOrDefault(s => s.Id == context.SessionId);
			if(session is null)
			{
				_logger.LogWarning("SessionModel not found for SDK session {SessionId}", context.SessionId);
				return Cancel();
			}

			ElicitationRequestModel request = ToRequestModel(context);

			_logger.LogInformation(
				"Elicitation request from '{Source}': Message='{Message}', Fields={FieldCount}, SessionId={SessionId}",
				request.ElicitationSource, request.Message, request.Fields.Length, request.SessionId);

			ElicitationResult result = await RequestElicitationAsync(request);

			_logger.LogDebug("Elicitation response: Action={Action}", result.Action.Value);
			return result;
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Error in elicitation handler");
			return Cancel();
		}
	}

	async Task<ElicitationResult> RequestElicitationAsync(ElicitationRequestModel request)
	{
		_pendingRequests[request.Id] = request;

		try
		{
			UpdateSessionOnElicitationRequested(request.SessionId, request);

			try
			{
				OnElicitationRequested?.Invoke(request.SessionId, request);
			}
			catch(Exception ex)
			{
				_logger.LogError(ex, "Subscriber exception in OnElicitationRequested for request {RequestId}", request.Id);
			}

			ElicitationResult result = await request.GetResponseAsync();
			return result;
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Error waiting for elicitation response");
			return Cancel();
		}
		finally
		{
			_pendingRequests.TryRemove(request.Id, out _);
		}
	}

	void UpdateSessionOnElicitationRequested(string sessionId, ElicitationRequestModel request)
	{
		SessionModel? session = _sessionStateProvider.Sessions.FirstOrDefault(s => s.Id == sessionId);
		if(session is null)
		{
			return;
		}

		_logger.LogInformation("Elicitation requested — adding request {RequestId} to session {SessionId}", request.Id, sessionId);

		lock(session.StatusHistoryLock)
		{
			if(!session.PendingElicitationRequests.TryAdd(request.Id, request))
			{
				_logger.LogWarning("Elicitation request {RequestId} already exists for session {SessionId}", request.Id, sessionId);
				return;
			}

			// Only push to history on the first blocking request across all three blocking types.
			if(session.Status is not SessionStatusEnum.NeedsPermission
				and not SessionStatusEnum.NeedsUserInput
				and not SessionStatusEnum.NeedsElicitation)
			{
				session.StatusHistory.Push(session.Status);
			}

			session.Status = SessionStatusEnum.NeedsElicitation;
		}

		_sessionStateProvider.NotifyStateChanged();
	}

	void UpdateSessionOnElicitationResolved(string sessionId, string requestId)
	{
		SessionModel? session = _sessionStateProvider.Sessions.FirstOrDefault(s => s.Id == sessionId);
		if(session is null)
		{
			return;
		}

		_logger.LogInformation("Elicitation resolved — removing request {RequestId} from session {SessionId}", requestId, sessionId);

		lock(session.StatusHistoryLock)
		{
			session.PendingElicitationRequests.TryRemove(requestId, out _);

			session.Status = !session.PendingPermissionRequests.IsEmpty
				? SessionStatusEnum.NeedsPermission
				: !session.PendingUserInputRequests.IsEmpty
					? SessionStatusEnum.NeedsUserInput
					: !session.PendingElicitationRequests.IsEmpty
						? SessionStatusEnum.NeedsElicitation
						: session.StatusHistory.TryPop(out SessionStatusEnum prev) ? prev : SessionStatusEnum.Idle;
		}

		_sessionStateProvider.NotifyStateChanged();
	}

	/// <summary>
	/// Resolves a pending elicitation request with the user's response.
	/// Pass <see langword="null"/> to cancel (equivalent to <c>Action = Cancel</c>).
	/// </summary>
	public void ResolveElicitationRequest(string requestId, ElicitationResult? result)
	{
		_logger.LogInformation("ResolveElicitationRequest: requestId={RequestId}, cancelled={Cancelled}", requestId, result is null);

		if(!_pendingRequests.TryGetValue(requestId, out ElicitationRequestModel? request))
		{
			_logger.LogWarning("No pending elicitation request found for {RequestId}. Pending count: {Count}", requestId, _pendingRequests.Count);
			return;
		}

		_logger.LogInformation("Resolving elicitation for session {SessionId}: {Message}", request.SessionId, request.Message);

		bool completed = request.CompletionSource.TrySetResult(result ?? Cancel());
		_logger.LogDebug("TCS completed: {Completed}", completed);

		if(!completed)
		{
			_logger.LogWarning("ResolveElicitationRequest: request {RequestId} was already resolved", requestId);
			return;
		}

		UpdateSessionOnElicitationResolved(request.SessionId, request.Id);
	}

	/// <summary>
	/// Cancels all pending elicitation requests for a session (e.g., when the session is deleted or disconnected).
	/// </summary>
	public void CancelPendingRequestsForSession(string sessionId)
	{
		List<string> requestIds = [.. _pendingRequests.Values
			.Where(r => r.SessionId == sessionId)
			.Select(r => r.Id)];

		foreach(string requestId in requestIds)
		{
			_logger.LogInformation("Cancelling elicitation request {RequestId} for session {SessionId}", requestId, sessionId);
			ResolveElicitationRequest(requestId, null);
		}
	}

	static ElicitationResult Cancel() => new()
	{
		Action = UIElicitationResponseAction.Cancel,
		Content = new Dictionary<string, object>()
	};
}
