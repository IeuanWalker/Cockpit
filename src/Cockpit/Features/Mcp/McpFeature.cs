using Cockpit.Features.Sessions;
using Cockpit.Features.Sessions.Models;
using GitHub.Copilot;
using GitHub.Copilot.Rpc;
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

	public async Task<List<McpServer>> LoadSessionMcpServersAsync(CopilotSession sdkSession, CancellationToken cancellationToken = default)
	{
		try
		{
			McpServerList result = await sdkSession.Rpc.Mcp.ListAsync(cancellationToken);
			_logger.LogInformation("Discovered {Count} MCP servers for session", result.Servers.Count);
			return [.. result.Servers];
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Failed to load MCP servers from SDK");
			return [];
		}
	}

	public Task EnableServerAsync(string sessionId, string serverName, CancellationToken cancellationToken = default) =>
		ExecuteAndRefreshAsync(
			sessionId,
			(s, ct) => s.Rpc.Mcp.EnableAsync(serverName, ct),
			"MCP enable",
			ex => _logger.LogError(ex, "Failed to enable MCP server {ServerName}", serverName),
			cancellationToken);

	public Task DisableServerAsync(string sessionId, string serverName, CancellationToken cancellationToken = default) =>
		ExecuteAndRefreshAsync(
			sessionId,
			(s, ct) => s.Rpc.Mcp.DisableAsync(serverName, ct),
			"MCP disable",
			ex => _logger.LogError(ex, "Failed to disable MCP server {ServerName}", serverName),
			cancellationToken);

	public Task ReloadAsync(string sessionId, CancellationToken cancellationToken = default) =>
		ExecuteAndRefreshAsync(
			sessionId,
			(s, ct) => s.Rpc.Mcp.ReloadAsync(ct),
			"MCP reload",
			ex => _logger.LogError(ex, "Failed to reload MCP servers"),
			cancellationToken);

	async Task ExecuteAndRefreshAsync(
		string sessionId,
		Func<CopilotSession, CancellationToken, Task> sdkOperation,
		string operationContext,
		Action<Exception> logFailure,
		CancellationToken cancellationToken)
	{
		if(!_sdkRegistry.TryGet(sessionId, out CopilotSession? sdkSession))
		{
			_logger.LogWarning("SDK session {SessionId} not found for {OperationContext}", sessionId, operationContext);
			return;
		}

		try
		{
			await sdkOperation(sdkSession, cancellationToken);
			await RefreshSessionMcpAsync(sessionId, sdkSession, cancellationToken);
		}
		catch(Exception ex)
		{
			logFailure(ex);
		}
	}

	async Task RefreshSessionMcpAsync(string sessionId, CopilotSession sdkSession, CancellationToken cancellationToken)
	{
		SessionModel? session = _sessionListFeature.Sessions.FirstOrDefault(s => s.Id == sessionId);
		if(session is null)
		{
			return;
		}

		session.Context.McpServers = await LoadSessionMcpServersAsync(sdkSession, cancellationToken);
		_sessionListFeature.NotifyStateChanged();
	}

	/// <summary>Returns a human-readable display string for the given MCP server status.</summary>
	public static string GetStatusDisplayString(McpServerStatus status)
	{
		if(status.Equals(McpServerStatus.Connected))
		{
			return "Connected";
		}

		if(status.Equals(McpServerStatus.Failed))
		{
			return "Failed";
		}

		if(status.Equals(McpServerStatus.NeedsAuth))
		{
			return "Needs Auth";
		}

		if(status.Equals(McpServerStatus.Pending))
		{
			return "Pending";
		}

		if(status.Equals(McpServerStatus.Disabled))
		{
			return "Disabled";
		}

		if(status.Equals(McpServerStatus.NotConfigured))
		{
			return "Not Configured";
		}

		return status.Value;
	}

	/// <summary>Returns the Tailwind CSS text-colour class for the given MCP server status.</summary>
	public static string GetStatusColor(McpServerStatus status)
	{
		if(status.Equals(McpServerStatus.Connected))
		{
			return "text-green-400";
		}

		if(status.Equals(McpServerStatus.Failed))
		{
			return "text-red-400";
		}

		if(status.Equals(McpServerStatus.Disabled))
		{
			return "secondary-text";
		}

		return "text-yellow-400";
	}
}
