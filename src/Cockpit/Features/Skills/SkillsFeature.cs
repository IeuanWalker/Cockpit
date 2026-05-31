using Cockpit.Features.Sessions;
using Cockpit.Features.Sessions.Models;
using GitHub.Copilot;
using GitHub.Copilot.Rpc;
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

	public async Task<List<Skill>> LoadSessionSkillsAsync(CopilotSession sdkSession, CancellationToken cancellationToken = default)
	{
		try
		{
			SkillList result = await sdkSession.Rpc.Skills.ListAsync(cancellationToken);
			_logger.LogInformation("Discovered {Count} skills for session", result.Skills.Count);
			return [.. result.Skills];
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Failed to load skills from SDK");
			return [];
		}
	}

	public Task EnableSkillAsync(string sessionId, string name, CancellationToken cancellationToken = default)
		=> ExecuteSkillOperationAsync(sessionId, $"enable:{name}", (s, ct) => s.Rpc.Skills.EnableAsync(name, ct), cancellationToken);

	public Task DisableSkillAsync(string sessionId, string name, CancellationToken cancellationToken = default)
		=> ExecuteSkillOperationAsync(sessionId, $"disable:{name}", (s, ct) => s.Rpc.Skills.DisableAsync(name, ct), cancellationToken);

	public Task ReloadAsync(string sessionId, CancellationToken cancellationToken = default)
		=> ExecuteSkillOperationAsync(sessionId, "reload", (s, ct) => s.Rpc.Skills.ReloadAsync(ct), cancellationToken);

	async Task ExecuteSkillOperationAsync(
		string sessionId,
		string operationName,
		Func<CopilotSession, CancellationToken, Task> sdkOp,
		CancellationToken cancellationToken)
	{
		if(!_sdkRegistry.TryGet(sessionId, out CopilotSession? sdkSession))
		{
			_logger.LogWarning("SDK session {SessionId} not found for skill operation {OperationName}", sessionId, operationName);
			return;
		}

		try
		{
			await sdkOp(sdkSession, cancellationToken);
			await RefreshSessionSkillsAsync(sessionId, sdkSession, cancellationToken);
		}
		catch(Exception ex) when(ex is not OperationCanceledException)
		{
			_logger.LogError(ex, "Skills operation {OperationName} failed", operationName);
		}
	}

	async Task RefreshSessionSkillsAsync(string sessionId, CopilotSession sdkSession, CancellationToken cancellationToken)
	{
		SessionModel? session = _sessionListFeature.Sessions.FirstOrDefault(s => s.Id == sessionId);
		if(session is null)
		{
			return;
		}

		session.Context.Skills = await LoadSessionSkillsAsync(sdkSession, cancellationToken);
		_sessionListFeature.NotifyStateChanged();
	}

	/// <summary>Groups skills by their source for display purposes.</summary>
	public static IReadOnlyDictionary<string, List<Skill>> GroupBySource(IEnumerable<Skill> skills)
		=> skills
			.GroupBy(s => string.IsNullOrWhiteSpace(s.Source.Value) ? "Unknown" : s.Source.Value, StringComparer.OrdinalIgnoreCase)
			.ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
}
