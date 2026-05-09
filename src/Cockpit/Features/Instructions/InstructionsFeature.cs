using GitHub.Copilot.SDK;
using GitHub.Copilot.SDK.Rpc;
using Microsoft.Extensions.Logging;

namespace Cockpit.Features.Instructions;

public sealed class InstructionsFeature
{
	readonly ILogger<InstructionsFeature> _logger;

	public InstructionsFeature(ILogger<InstructionsFeature> logger)
	{
		_logger = logger;
	}

	public async Task<List<InstructionsSources>> LoadSessionInstructionsAsync(CopilotSession sdkSession, CancellationToken cancellationToken = default)
	{
		try
		{
			InstructionsGetSourcesResult result = await sdkSession.Rpc.Instructions.GetSourcesAsync(cancellationToken);
			_logger.LogInformation("Discovered {Count} instruction sources for session", result.Sources.Count);
			return [.. result.Sources];
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to load instructions from SDK");
			return [];
		}
	}

	/// <summary>Groups instruction sources by their location for display purposes.</summary>
	public static IReadOnlyDictionary<string, List<InstructionsSources>> GroupByLocation(IEnumerable<InstructionsSources> sources)
		=> sources
			.GroupBy(s => s.Location.ToString(), StringComparer.OrdinalIgnoreCase)
			.ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
}
