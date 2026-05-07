using System.Text;
using Cockpit.Features.Sessions.Models;

namespace Cockpit.Features.SlashCommands;

public sealed class SlashCommandFeature
{
	readonly Dictionary<string, RegisteredSlashCommand> _commands;

	public SlashCommandFeature()
	{
		RegisteredSlashCommand[] builtInCommands =
		[
			new RegisteredSlashCommand(
				new SlashCommandDefinition
				{
					Name = "help",
					Description = "List available slash commands",
					Usage = "/help"
				},
				HandleHelp),
			new RegisteredSlashCommand(
				new SlashCommandDefinition
				{
					Name = "session",
					Description = "Show details for the current chat session",
					Usage = "/session"
				},
				HandleSession)
		];

		Commands = [.. builtInCommands.Select(command => command.Definition)];

		_commands = new Dictionary<string, RegisteredSlashCommand>(StringComparer.OrdinalIgnoreCase);
		foreach(RegisteredSlashCommand command in builtInCommands)
		{
			_commands[command.Definition.Name] = command;
			foreach(string alias in command.Definition.Aliases)
			{
				_commands[alias] = command;
			}
		}
	}

	public IReadOnlyList<SlashCommandDefinition> Commands { get; }

	public bool TryHandle(SessionModel session, string input, out SlashCommandResult? result)
	{
		result = null;
		if(string.IsNullOrWhiteSpace(input) || !input.StartsWith('/'))
		{
			return false;
		}

		List<string> tokens = Tokenize(input[1..]);
		if(tokens.Count == 0)
		{
			result = new SlashCommandResult
			{
				Success = false,
				Message = "Empty slash command. Try /help."
			};
			return true;
		}

		string commandName = tokens[0];
		if(!_commands.TryGetValue(commandName, out RegisteredSlashCommand? command))
		{
			result = new SlashCommandResult
			{
				Success = false,
				Message = $"Unknown command '/{commandName}'. Try /help."
			};
			return true;
		}

		SlashCommandContext context = new()
		{
			RawInput = input,
			CommandName = commandName,
			Arguments = [.. tokens.Skip(1)],
			Session = session
		};
		result = command.Handler(context);
		return true;
	}

	SlashCommandResult HandleHelp(SlashCommandContext _)
	{
		IEnumerable<string> commandLines = Commands
			.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
			.Select(c => $"- `{c.Usage}` — {c.Description}");

		return new SlashCommandResult
		{
			Success = true,
			Message = $"Available slash commands:\n{string.Join("\n", commandLines)}"
		};
	}

	static SlashCommandResult HandleSession(SlashCommandContext context)
	{
		if(context.Arguments.Count > 0)
		{
			return new SlashCommandResult
			{
				Success = false,
				Message = "Usage: /session"
			};
		}

		SessionModel session = context.Session;
		StringBuilder builder = new();
		builder.AppendLine("Current session:");
		builder.AppendLine($"- Id: `{session.Id}`");
		builder.AppendLine($"- Title: {session.Title}");
		builder.AppendLine($"- Model: `{session.Model.Id}`");
		if(!string.IsNullOrWhiteSpace(session.Context.CurrentWorkingDirectory))
		{
			builder.AppendLine($"- Working directory: `{session.Context.CurrentWorkingDirectory}`");
		}
		if(!string.IsNullOrWhiteSpace(session.Context.Branch))
		{
			builder.AppendLine($"- Branch: `{session.Context.Branch}`");
		}

		return new SlashCommandResult
		{
			Success = true,
			Message = builder.ToString().TrimEnd()
		};
	}

	static List<string> Tokenize(string input)
	{
		List<string> result = [];
		StringBuilder current = new();
		bool inQuotes = false;

		foreach(char ch in input)
		{
			if(ch == '"')
			{
				inQuotes = !inQuotes;
				continue;
			}

			if(char.IsWhiteSpace(ch) && !inQuotes)
			{
				if(current.Length > 0)
				{
					result.Add(current.ToString());
					current.Clear();
				}
				continue;
			}

			current.Append(ch);
		}

		if(current.Length > 0)
		{
			result.Add(current.ToString());
		}

		return result;
	}

	record RegisteredSlashCommand(
		SlashCommandDefinition Definition,
		Func<SlashCommandContext, SlashCommandResult> Handler
	);
}
