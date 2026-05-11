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
		catch(OperationCanceledException)
		{
			throw;
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
		catch(OperationCanceledException)
		{
			throw;
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

	/// <summary>Returns the Tailwind CSS text-colour class for the given MCP server status.</summary>
	public static string GetStatusColor(McpServerStatus status) => status switch
	{
		McpServerStatus.Connected => "text-green-400",
		McpServerStatus.Failed => "text-red-400",
		McpServerStatus.Disabled => "secondary-text",
		_ => "text-yellow-400"
	};
}
