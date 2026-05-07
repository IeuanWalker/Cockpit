using Cockpit.Features.Sessions.Models;

namespace Cockpit.Features.SlashCommands;

public sealed class SlashCommandDefinition
{
	public required string Name { get; init; }
	public string Description { get; init; } = string.Empty;
	public string Usage { get; init; } = string.Empty;
	public string[] Aliases { get; init; } = [];
}

public sealed class SlashCommandContext
{
	public required string RawInput { get; init; }
	public required string CommandName { get; init; }
	public required IReadOnlyList<string> Arguments { get; init; }
	public required SessionModel Session { get; init; }
}

public sealed class SlashCommandResult
{
	public bool Success { get; init; }
	public string Message { get; init; } = string.Empty;
}
