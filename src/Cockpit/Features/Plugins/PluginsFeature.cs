using GitHub.Copilot.SDK;
using GitHub.Copilot.SDK.Rpc;
using Microsoft.Extensions.Logging;
using SdkPlugin = GitHub.Copilot.SDK.Rpc.Plugin;

namespace Cockpit.Features.Plugins;

public sealed class PluginsFeature
{
	readonly ILogger<PluginsFeature> _logger;

	public PluginsFeature(ILogger<PluginsFeature> logger)
	{
		_logger = logger;
	}

	/// <summary>
	/// Queries the SDK for all plugins associated with <paramref name="sdkSession"/>.
	/// Returns an empty list on SDK failure; re-throws on cancellation.
	/// </summary>
#pragma warning disable GHCP001
	public async Task<List<SdkPlugin>> LoadSessionPluginsAsync(CopilotSession sdkSession, CancellationToken cancellationToken = default)
	{
		try
		{
			PluginList result = await sdkSession.Rpc.Plugins.ListAsync(cancellationToken);
			_logger.LogInformation("Discovered {Count} plugins for session", result.Plugins.Count);
			return [.. result.Plugins];
		}
		catch(OperationCanceledException)
		{
			throw;
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Failed to load plugins from SDK");
			return [];
		}
	}
#pragma warning restore GHCP001

	/// <summary>Returns a formatted version string, falling back to <c>"unknown"</c> when <paramref name="version"/> is <see langword="null"/>, empty, or whitespace.</summary>
	public static string FormatVersion(string? version)
		=> string.IsNullOrWhiteSpace(version) ? "unknown" : version;
}
