using Cockpit.Features.Sessions.Models;
using GitHub.Copilot;
using Microsoft.Extensions.Logging;
using SdkSessionMetadata = GitHub.Copilot.SessionMetadata;

namespace Cockpit.Features.Sessions;

public sealed partial class SessionFeature
{
	Task? _loadExistingSessionsTask;
	readonly Lock _loadGate = new();

	public Task LoadExistingSessions()
	{
		lock(_loadGate)
		{
			if(_loadExistingSessionsTask is null || _loadExistingSessionsTask.IsCanceled || _loadExistingSessionsTask.IsFaulted)
			{
				_loadExistingSessionsTask = RefreshExistingSessions();
			}

			return _loadExistingSessionsTask;
		}
	}

	public async Task RefreshExistingSessions()
	{
		try
		{
			_logger.LogInformation("Loading existing sessions from SDK...");

			CopilotClient client = await _clientFeature.GetClientAsync();

			Task<ModelInfo> defaultModelTask = _modelFeature.GetDefaultModel().AsTask();

			IList<SdkSessionMetadata> sessionMetadataList;
			try
			{
				sessionMetadataList = await client.ListSessionsAsync();
			}
			catch
			{
				// Ensure any failure from the overlapped model fetch is observed on the error path.
				try { await defaultModelTask; } catch { /* ignore */ }
				throw;
			}

			ModelInfo defaultModel = await defaultModelTask;
			if(sessionMetadataList.Count == 0)
			{
				_logger.LogInformation("No existing sessions found");
				return;
			}

			_logger.LogInformation("Found {Count} existing sessions", sessionMetadataList.Count);

			PopulateSessionsFromMetadata(sessionMetadataList, defaultModel, _sessionListFeature, _logger);

			_sessionListFeature.NotifyStateChanged(null, SessionChangeKind.SessionCollection);
			_logger.LogInformation("Successfully loaded {Count} sessions", _sessionListFeature.Sessions.Count);
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Failed to load existing sessions");
		}
	}

	/// <summary>
	/// Materializes <paramref name="sessionMetadataList"/> into <see cref="SessionModel"/> instances and
	/// adds the not-yet-known ones to <paramref name="sessionListFeature"/>. Extracted as an
	/// <see langword="internal static"/> method (with no SDK/network dependencies) so it can be unit
	/// tested and benchmarked directly. Sessions already present (matched by id) are skipped.
	/// </summary>
	internal static void PopulateSessionsFromMetadata(
		IList<SdkSessionMetadata> sessionMetadataList,
		ModelInfo defaultModel,
		SessionListFeature sessionListFeature,
		ILogger logger)
	{
		IReadOnlyList<SessionModel> existing = sessionListFeature.Sessions;
		HashSet<string> seenSessionIds = new(existing.Count + sessionMetadataList.Count, StringComparer.Ordinal);
		foreach(SessionModel session in existing)
		{
			seenSessionIds.Add(session.Id);
		}

		List<SessionModel> newSessions = new(sessionMetadataList.Count);
		SessionWorkingDirectoryNormalizer.LaunchDirectories launchDirectories = SessionWorkingDirectoryNormalizer.LaunchDirectories.Capture();
		foreach(SdkSessionMetadata metadata in sessionMetadataList)
		{
			// Add returns false when the id is already known (existing session or duplicate
			// in the incoming batch), mirroring the original per-item membership check.
			if(!seenSessionIds.Add(metadata.SessionId))
			{
				continue;
			}

			try
			{
				newSessions.Add(CreateExistingSessionModel(metadata, defaultModel, launchDirectories));
			}
			catch(Exception ex)
			{
				// Remove the id from seenSessionIds on failure to restore the original retry-on-failure
				// behavior: a duplicate entry later in this batch will be attempted again rather than
				// skipped, and the session won't be permanently marked as seen.
				seenSessionIds.Remove(metadata.SessionId);
				logger.LogWarning(ex, "Failed to load session {SessionId}", metadata.SessionId);
			}
		}

		sessionListFeature.AddSessionsAtFront(newSessions);

		if(logger.IsEnabled(LogLevel.Information))
		{
			foreach(SessionModel session in newSessions)
			{
				logger.LogInformation("Loaded session {SessionId}", session.Id);
			}
		}
	}

	static SessionModel CreateExistingSessionModel(SdkSessionMetadata metadata, ModelInfo defaultModel, in SessionWorkingDirectoryNormalizer.LaunchDirectories launchDirectories)
	{
		// Normalize once against pre-captured launch directories. The original code additionally
		// called ApplyContextConsistency, which re-normalized (idempotent) and nulled the Git
		// fields when the cwd was null — both effects are already produced by the single Normalize
		// call and the conditional assignments below, so the redundant second normalization is
		// dropped.
		string? cwd = SessionWorkingDirectoryNormalizer.Normalize(metadata.Context?.WorkingDirectory, launchDirectories);

		return new SessionModel
		{
			Id = metadata.SessionId,
			Title = metadata.Summary ?? $"Session {metadata.SessionId[..8]}",
			CreatedAt = metadata.StartTime.UtcDateTime,
			LastActivity = metadata.ModifiedTime.UtcDateTime,
			AgentRunState = AgentRunStateEnum.Idle,
			Model = defaultModel,
			ReasoningEffort = defaultModel.DefaultReasoningEffort,
			Context = new()
			{
				CurrentWorkingDirectory = cwd,
				WorkspacePath = null,
				GitRoot = cwd is null ? null : metadata.Context?.GitRoot,
				Repository = cwd is null ? null : metadata.Context?.Repository,
				Branch = cwd is null ? null : metadata.Context?.Branch
			}
		};
	}

}
