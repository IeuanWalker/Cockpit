using Cockpit.Features.Sessions;
using Cockpit.Features.Sessions.Models;
using GitHub.Copilot.SDK;
using GitHub.Copilot.SDK.Rpc;
using Microsoft.Extensions.Logging;

namespace Cockpit.Features.Skills;

public sealed class SkillsFeature
{
	readonly ILogger<SkillsFeature> _logger;
	readonly SdkSessionRegistry _sdkRegistry;
	readonly SessionListFeature _sessionListFeature;

	public SkillsFeature(ILogger<SkillsFeature> logger, SdkSessionRegistry sdkRegistry, SessionListFeature sessionListFeature)
	{
		_logger = logger;
		_sdkRegistry = sdkRegistry;
		_sessionListFeature = sessionListFeature;
	}

#pragma warning disable GHCP001
	public async Task<List<Skill>> LoadSessionSkillsAsync(CopilotSession sdkSession, CancellationToken cancellationToken = default)
	{
		try
		{
			SkillList result = await sdkSession.Rpc.Skills.ListAsync(cancellationToken);
			_logger.LogInformation("Discovered {Count} skills for session", result.Skills.Count);
			return [.. result.Skills];
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to load skills from SDK");
			return [];
		}
	}

	public async Task EnableSkillAsync(string sessionId, string name, CancellationToken cancellationToken = default)
	{
		if (!_sdkRegistry.TryGet(sessionId, out CopilotSession? sdkSession))
		{
			_logger.LogWarning("SDK session {SessionId} not found for skill enable", sessionId);
			return;
		}

		try
		{
			await sdkSession.Rpc.Skills.EnableAsync(name, cancellationToken);
			await RefreshSessionSkillsAsync(sessionId, sdkSession, cancellationToken);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to enable skill {Name}", name);
		}
	}

	public async Task DisableSkillAsync(string sessionId, string name, CancellationToken cancellationToken = default)
	{
		if (!_sdkRegistry.TryGet(sessionId, out CopilotSession? sdkSession))
		{
			_logger.LogWarning("SDK session {SessionId} not found for skill disable", sessionId);
			return;
		}

		try
		{
			await sdkSession.Rpc.Skills.DisableAsync(name, cancellationToken);
			await RefreshSessionSkillsAsync(sessionId, sdkSession, cancellationToken);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to disable skill {Name}", name);
		}
	}

	public async Task ReloadAsync(string sessionId, CancellationToken cancellationToken = default)
	{
		if (!_sdkRegistry.TryGet(sessionId, out CopilotSession? sdkSession))
		{
			_logger.LogWarning("SDK session {SessionId} not found for skills reload", sessionId);
			return;
		}

		try
		{
			await sdkSession.Rpc.Skills.ReloadAsync(cancellationToken);
			await RefreshSessionSkillsAsync(sessionId, sdkSession, cancellationToken);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to reload skills");
		}
	}

	async Task RefreshSessionSkillsAsync(string sessionId, CopilotSession sdkSession, CancellationToken cancellationToken)
	{
		SessionModel? session = _sessionListFeature.Sessions.FirstOrDefault(s => s.Id == sessionId);
		if (session is null) return;

		session.Context.Skills = await LoadSessionSkillsAsync(sdkSession, cancellationToken);
		_sessionListFeature.NotifyStateChanged();
	}
#pragma warning restore GHCP001

	/// <summary>Groups skills by their source for display purposes.</summary>
	public static IReadOnlyDictionary<string, List<Skill>> GroupBySource(IEnumerable<Skill> skills)
		=> skills
			.GroupBy(s => string.IsNullOrWhiteSpace(s.Source) ? "Unknown" : s.Source, StringComparer.OrdinalIgnoreCase)
			.ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
}
