using Cockpit.Features.Sessions;
using Cockpit.Features.Sessions.Models;
using GitHub.Copilot.SDK;
using GitHub.Copilot.SDK.Rpc;
using Microsoft.Extensions.Logging;

namespace Cockpit.Features.Mcp;

public sealed class McpFeature
{
	readonly ILogger<McpFeature> _logger;
	readonly SdkSessionRegistry _sdkRegistry;
	readonly SessionListFeature _sessionListFeature;

	public McpFeature(ILogger<McpFeature> logger, SdkSessionRegistry sdkRegistry, SessionListFeature sessionListFeature)
	{
		_logger = logger;
		_sdkRegistry = sdkRegistry;
		_sessionListFeature = sessionListFeature;
	}

#pragma warning disable GHCP001
	public async Task<List<McpServer>> LoadSessionMcpServersAsync(CopilotSession sdkSession, CancellationToken cancellationToken = default)
	{
		try
		{
			McpServerList result = await sdkSession.Rpc.Mcp.ListAsync(cancellationToken);
			_logger.LogInformation("Discovered {Count} MCP servers for session", result.Servers.Count);
			return [.. result.Servers];
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to load MCP servers from SDK");
			return [];
		}
	}

	public async Task EnableServerAsync(string sessionId, string serverName, CancellationToken cancellationToken = default)
	{
		if (!_sdkRegistry.TryGet(sessionId, out CopilotSession? sdkSession))
		{
			_logger.LogWarning("SDK session {SessionId} not found for MCP enable", sessionId);
			return;
		}

		try
		{
			await sdkSession.Rpc.Mcp.EnableAsync(serverName, cancellationToken);
			await RefreshSessionMcpAsync(sessionId, sdkSession, cancellationToken);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to enable MCP server {ServerName}", serverName);
		}
	}

	public async Task DisableServerAsync(string sessionId, string serverName, CancellationToken cancellationToken = default)
	{
		if (!_sdkRegistry.TryGet(sessionId, out CopilotSession? sdkSession))
		{
			_logger.LogWarning("SDK session {SessionId} not found for MCP disable", sessionId);
			return;
		}

		try
		{
			await sdkSession.Rpc.Mcp.DisableAsync(serverName, cancellationToken);
			await RefreshSessionMcpAsync(sessionId, sdkSession, cancellationToken);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to disable MCP server {ServerName}", serverName);
		}
	}

	public async Task ReloadAsync(string sessionId, CancellationToken cancellationToken = default)
	{
		if (!_sdkRegistry.TryGet(sessionId, out CopilotSession? sdkSession))
		{
			_logger.LogWarning("SDK session {SessionId} not found for MCP reload", sessionId);
			return;
		}

		try
		{
			await sdkSession.Rpc.Mcp.ReloadAsync(cancellationToken);
			await RefreshSessionMcpAsync(sessionId, sdkSession, cancellationToken);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to reload MCP servers");
		}
	}

	async Task RefreshSessionMcpAsync(string sessionId, CopilotSession sdkSession, CancellationToken cancellationToken)
	{
		SessionModel? session = _sessionListFeature.Sessions.FirstOrDefault(s => s.Id == sessionId);
		if (session is null) return;

		session.Context.McpServers = await LoadSessionMcpServersAsync(sdkSession, cancellationToken);
		_sessionListFeature.NotifyStateChanged();
	}
#pragma warning restore GHCP001

	/// <summary>Returns a human-readable display string for the given MCP server status.</summary>
	public static string GetStatusDisplayString(McpServerStatus status) => status switch
	{
		McpServerStatus.Connected => "Connected",
		McpServerStatus.Failed => "Failed",
		McpServerStatus.NeedsAuth => "Needs Auth",
		McpServerStatus.Pending => "Pending",
		McpServerStatus.Disabled => "Disabled",
		McpServerStatus.NotConfigured => "Not Configured",
		_ => status.ToString()
	};
}
