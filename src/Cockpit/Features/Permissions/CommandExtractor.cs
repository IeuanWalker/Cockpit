using System.Text;
using System.Text.RegularExpressions;

namespace Cockpit.Features.Permissions;

/// <summary>
/// Utility for extracting executables from shell commands.
/// Based on Cooper's extractExecutables.ts implementation.
/// </summary>
public static partial class CommandExtractor
{
	// Generated regex patterns for performance
	[GeneratedRegex(@"\\u([0-9A-Fa-f]{4})")]
	private static partial Regex UnicodeEscapePattern();

	[GeneratedRegex(@"<<\s*['""']?(\w+)['""']?[\s\S]*?\n\1\s*(?=\n|$)")]
	private static partial Regex HeredocWithMarkerPattern();

	[GeneratedRegex(@"<<\s*['""']?\w+['""']?[\s\S]*$")]
	private static partial Regex HeredocEndPattern();

	[GeneratedRegex(@"""[^""]*""|'[^']*'|`[^`]*`")]
	private static partial Regex StringLiteralsPattern();

	[GeneratedRegex(@"#[^\n]*|\d*>&?\d+|\d+>>\S+|\d+>\S+")]
	private static partial Regex CommentsAndRedirectionsPattern();

	[GeneratedRegex(@"^[A-Z]+$")]
	private static partial Regex HeredocMarkerPattern();

	[GeneratedRegex(@"^[<>|&;()]+$")]
	private static partial Regex PunctuationPattern();

	[GeneratedRegex(@"^(?=.*[a-zA-Z])[a-zA-Z0-9_\-\.]+$")]
	private static partial Regex CommandValidationPattern();

	[GeneratedRegex(@"^[a-zA-Z0-9_\-]+$")]
	private static partial Regex SubcommandValidationPattern();

	[GeneratedRegex(@"^(basename|dirname)\s+\$\(")]
	private static partial Regex UtilityCommandPattern();

	[GeneratedRegex(@"(?:^|(?:sudo|env|nohup|nice|time|command)\s+)*(rm|rmdir|unlink|shred)(?:\s|$)(.*)")]
	private static partial Regex RmCommandPattern();

	// Commands that should include their subcommand for granular permission control
	static readonly HashSet<string> subcommandExecutables = ["git", "npm", "yarn", "pnpm", "docker", "kubectl", "gh", "dotnet", "cargo"];

	// Shell builtins that are not real executables and should be skipped
	static readonly HashSet<string> shellBuiltinsToSkip = ["true", "false"];

	// Shell keywords that are part of control flow syntax, not executables
	static readonly HashSet<string> shellKeywordsToSkip = ["for", "in", "do", "done", "while", "until", "if", "then", "else", "elif", "fi", "case", "esac", "select"];

	// Flags that are commonly followed by values that might look like commands
	// Only the most common cases - keeps the list minimal
	static readonly HashSet<string> flagsWithValues = ["-n", "-f", "-C", "-t"];

	// Destructive executables that should NEVER be auto-approved
	static readonly HashSet<string> destructiveExecutables =
	[
		"rm", "rmdir", "unlink", "shred",
		"git reset", "git clean", "git push --force", "git push -f",
		"dd", "mkfs", "fdisk", "parted"
	];

	// Common prefixes to skip
	static readonly HashSet<string> prefixes = ["sudo", "env", "nohup", "nice", "time", "command"];

	// Navigation/setup commands that don't need permission (informational only)
	static readonly HashSet<string> navigationCommands = ["cd", "pushd", "popd", "pwd"];

	/// <summary>
	/// Decode Unicode escape sequences and common escape sequences in a string (e.g., \u0027 -> ', \n -> newline)
	/// </summary>
	static string DecodeUnicodeEscapes(string input)
	{
		// Decode Unicode escapes and common escape sequences
		return UnicodeEscapePattern().Replace(input, m => ((char)Convert.ToInt32(m.Groups[1].Value, 16)).ToString())
									 .Replace("\\n", "\n")
									 .Replace("\\r", "\r")
									 .Replace("\\t", "\t");
	}

	/// <summary>
	/// Process command substitutions $(...)  - remove the wrapper but keep content for pipeline commands
	/// Standalone utility commands like basename/dirname in nested substitutions are removed
	/// </summary>
	static string ProcessCommandSubstitutions(string input)
	{
		StringBuilder result = new(input.Length);
		int i = 0;

		while(i < input.Length)
		{
			// Check for $(...) command substitution
			if(i < input.Length - 1 && input[i] == '$' && input[i + 1] == '(')
			{
				i += 2;
				int start = i;
				int depth = 1;

				// Find the matching )
				while(i < input.Length && depth > 0)
				{
					char c = input[i];
					if(c == '(')
					{
						depth++;
					}
					else if(c == ')')
					{
						if(--depth == 0)
						{
							// Extract the content between $( and )
							string content = input[start..i];

							// Check if this is a simple utility command that wraps another substitution
							if(!UtilityCommandPattern().IsMatch(content.Trim()))
							{
								// Keep the content (remove the $() wrapper)
								result.Append(' ').Append(content).Append(' ');
							}
							i++;
							break;
						}
					}
					i++;
				}
				continue;
			}

			result.Append(input[i]);
			i++;
		}

		return result.ToString();
	}

	/// <summary>
	/// Extract commands from inside parenthesized subexpressions like (Get-Item ...)
	/// Returns the input with parentheses converted to spaces so commands inside can be extracted
	/// </summary>
	static string ExpandParentheses(string input)
	{
		// Replace parentheses with spaces to allow extraction of commands inside them
		// But keep the content so commands can be found
		return input.Replace('(', ' ').Replace(')', ' ');
	}

	/// <summary>
	/// Remove PowerShell scriptblocks passed as parameters (e.g., in [regex]::Replace or ForEach-Object),
	/// but keep control flow braces (for, if, while, etc.).
	/// For cmdlets like ForEach-Object, removes the cmdlet name and braces but keeps the content
	/// For other scriptblocks (like in [regex]::Replace), removes everything
	/// </summary>
	static string RemoveScriptblocks(string input)
	{
		// Common cmdlets that take scriptblocks as their primary parameter
		HashSet<string> cmdletsKeepContent = new(StringComparer.OrdinalIgnoreCase)
		{
			"ForEach-Object", "Where-Object", "Measure-Command"
		};

		HashSet<string> cmdletsRemoveContent = new(StringComparer.OrdinalIgnoreCase)
		{
			"Invoke-Command"
		};

		StringBuilder result = new(input.Length);
		int i = 0;

		while(i < input.Length)
		{
			if(input[i] == '{')
			{
				// Look back to see if this is a scriptblock parameter
				int lookback = i - 1;
				while(lookback >= 0 && char.IsWhiteSpace(input[lookback]))
				{
					lookback--;
				}

				bool isScriptblockParam = lookback >= 0 && (input[lookback] == ',' || input[lookback] == '(');
				bool keepContent = false;

				// Also check if preceded by a cmdlet name that takes scriptblocks
				if(!isScriptblockParam && lookback >= 0)
				{
					int wordEnd = lookback + 1;
					int wordStart = lookback;
					while(wordStart > 0 && (char.IsLetterOrDigit(input[wordStart - 1]) || input[wordStart - 1] == '-'))
					{
						wordStart--;
					}

					if(wordStart < wordEnd)
					{
						string word = input[wordStart..wordEnd];
						if(cmdletsKeepContent.Contains(word))
						{
							isScriptblockParam = keepContent = true;
							result.Length = Math.Max(0, result.Length - (wordEnd - wordStart));
						}
						else if(cmdletsRemoveContent.Contains(word))
						{
							isScriptblockParam = true;
							result.Length = Math.Max(0, result.Length - (wordEnd - wordStart));
						}
					}
				}

				if(isScriptblockParam)
				{
					int depth = 1;
					i++; // skip opening brace

					while(i < input.Length && depth > 0)
					{
						char c = input[i];
						if(c == '{')
						{
							depth++;
						}
						else if(c == '}')
						{
							if(--depth == 0)
							{
								i++;
								break;
							}
						}

						if(keepContent && depth > 0)
						{
							result.Append(c);
						}

						i++;
					}
					continue;
				}
			}

			result.Append(input[i]);
			i++;
		}

		return result.ToString();
	}

	/// <summary>
	/// Extract all executables from a shell command.
	/// Handles heredocs, string literals, redirections, and common shell patterns.
	/// </summary>
	public static List<string> ExtractExecutables(string command)
	{
		HashSet<string> executables = [];

		// Decode Unicode escape sequences first (e.g., \u0027 -> ')
		command = DecodeUnicodeEscapes(command);

		// Remove heredocs first (<<'MARKER' ... MARKER or <<MARKER ... MARKER)
		string cleaned = HeredocWithMarkerPattern().Replace(command, "");
		cleaned = HeredocEndPattern().Replace(cleaned, "");

		// Remove string literals first to avoid false positives
		cleaned = StringLiteralsPattern().Replace(cleaned, m => new string(m.Value[0], 2));

		// Process command substitutions like $(...)
		cleaned = ProcessCommandSubstitutions(cleaned);

		// Then remove PowerShell scriptblocks { ... } passed as parameters
		cleaned = RemoveScriptblocks(cleaned);

		// Expand parentheses to extract commands from subexpressions like (Get-Item ...)
		cleaned = ExpandParentheses(cleaned);

		// Remove shell comments and redirections
		cleaned = CommentsAndRedirectionsPattern().Replace(cleaned, "");

		// Split on shell operators, separators, and newlines
		string[] segments = cleaned.Split([';', '&', '|', '\n'], StringSplitOptions.RemoveEmptyEntries);

		foreach(string segment in segments)
		{
			string trimmed = segment.Trim();
			// Skip if empty or looks like a heredoc marker line
			if(string.IsNullOrWhiteSpace(trimmed) || HeredocMarkerPattern().IsMatch(trimmed))
			{
				continue;
			}

			// Get parts of segment
			string[] parts = trimmed.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);

			string? foundExec = null;
			string? subcommand = null;
			bool skipNextAsLoopVar = false;
			bool inForValueList = false;
			bool skipNextAsFlagValue = false;

			for(int i = 0; i < parts.Length; i++)
			{
				string part = parts[i];

				// If we're in a "for VAR in VALUE_LIST" context, skip until we hit 'do'
				if(inForValueList)
				{
					if(part == "do")
					{
						inForValueList = false;
					}

					continue;
				}

				// Skip value after a flag that takes one
				if(skipNextAsFlagValue)
				{
					skipNextAsFlagValue = false;
					continue;
				}

				// Skip the loop variable after 'for' or 'select'
				if(skipNextAsLoopVar)
				{
					skipNextAsLoopVar = false;
					continue;
				}

				// Skip environment variable assignments, flags, prefixes, builtins, and keywords
				if((part.Contains('=') && !part.StartsWith('-')) ||
				   part.StartsWith('-') ||
				   part.StartsWith('>') ||
				   part.StartsWith('<') ||
				   string.IsNullOrEmpty(part))
				{
					if(part.StartsWith('-') && flagsWithValues.Contains(part))
					{
						skipNextAsFlagValue = true;
					}
					continue;
				}

				// Check prefixes, builtins, and keywords
				if(prefixes.Contains(part) || shellBuiltinsToSkip.Contains(part))
				{
					continue;
				}

				if(shellKeywordsToSkip.Contains(part))
				{
					if(part is "for" or "select")
					{
						skipNextAsLoopVar = true;
					}
					else if(part == "in" && !skipNextAsLoopVar)
					{
						inForValueList = true;
					}
					continue;
				}

				// Skip punctuation-only tokens
				if(PunctuationPattern().IsMatch(part))
				{
					continue;
				}

				// Found potential executable - extract filename from path
				int lastSlash = part.LastIndexOf('/');
				int lastBackslash = part.LastIndexOf('\\');
				int pathSepIndex = Math.Max(lastSlash, lastBackslash);
				string exec = pathSepIndex >= 0 ? part[(pathSepIndex + 1)..] : part;

				// Validate it looks like a command with at least one letter
				if(!string.IsNullOrEmpty(exec) && CommandValidationPattern().IsMatch(exec))
				{
					if(foundExec == null)
					{
						foundExec = exec;

						// Check if this needs subcommand handling
						if(subcommandExecutables.Contains(exec))
						{
							// Look for subcommand in next non-flag part
							bool skipNextSubValue = false;
							for(int j = i + 1; j < parts.Length; j++)
							{
								string nextPart = parts[j];
								if(skipNextSubValue)
								{
									skipNextSubValue = false;
									continue;
								}
								if(nextPart.StartsWith('-'))
								{
									if(flagsWithValues.Contains(nextPart))
									{
										skipNextSubValue = true;
									}

									continue;
								}
								if(nextPart.Contains('='))
								{
									continue;
								}
								// Found potential subcommand
								if(SubcommandValidationPattern().IsMatch(nextPart))
								{
									subcommand = nextPart;
									break;
								}
								break;
							}
						}
					}
					break;
				}
			}

			if(foundExec != null)
			{
				// Combine executable with subcommand for granular control
				executables.Add(subcommand != null ? $"{foundExec} {subcommand}" : foundExec);
			}
		}

		return [.. executables];
	}

	/// <summary>
	/// Filter out navigation/informational commands, returning only commands that require permissions.
	/// Useful for permission UI to show only the "meaningful" operations.
	/// </summary>
	public static List<string> ExtractMeaningfulExecutables(string command)
	{
		List<string> all = ExtractExecutables(command);
		return [.. all.Where(cmd => !navigationCommands.Contains(cmd))];
	}

	/// <summary>
	/// Check if an executable identifier is destructive (can delete files/data).
	/// </summary>
	public static bool IsDestructiveExecutable(string executableId)
	{
		return destructiveExecutables.Contains(executableId);
	}

	/// <summary>
	/// Check if a shell command contains any destructive operations.
	/// Returns true if the command could delete files or destroy data.
	/// </summary>
	public static bool ContainsDestructiveCommand(string command)
	{
		List<string> executables = ExtractExecutables(command);

		// Check if any executable is destructive
		foreach(string exec in executables)
		{
			if(IsDestructiveExecutable(exec))
			{
				return true;
			}
		}

		// Special case: git push with --force or -f flag
		string commandLower = command.ToLowerInvariant();
		if(commandLower.Contains("git push") &&
			(commandLower.Contains("--force") || commandLower.Contains(" -f")))
		{
			return true;
		}

		// Special case: 'find' with -delete flag or -exec rm
		if(commandLower.Contains("find ") &&
			(commandLower.Contains("-delete") ||
			 commandLower.Contains("-exec rm") ||
			 commandLower.Contains("-exec /bin/rm") ||
			 commandLower.Contains("-exec /usr/bin/rm")))
		{
			return true;
		}

		// Special case: xargs with rm
		if(commandLower.Contains("xargs rm") ||
			commandLower.Contains("xargs /bin/rm") ||
			commandLower.Contains("xargs /usr/bin/rm"))
		{
			return true;
		}

		return false;
	}

	/// <summary>
	/// Get a list of destructive executables found in a command.
	/// </summary>
	public static List<string> GetDestructiveExecutables(string command)
	{
		List<string> executables = ExtractExecutables(command);
		List<string> destructive = [];

		foreach(string exec in executables)
		{
			if(IsDestructiveExecutable(exec))
			{
				destructive.Add(exec);
			}
		}

		// Special case: find with -delete/-exec rm
		string commandLower = command.ToLowerInvariant();
		if(commandLower.Contains("find ") &&
			(commandLower.Contains("-delete") ||
			 commandLower.Contains("-exec rm") ||
			 commandLower.Contains("-exec /bin/rm") ||
			 commandLower.Contains("-exec /usr/bin/rm")))
		{
			if(!destructive.Contains("find -delete"))
			{
				destructive.Add("find -delete");
			}
		}

		// Special case: xargs with rm
		if(commandLower.Contains("xargs rm") ||
			commandLower.Contains("xargs /bin/rm") ||
			commandLower.Contains("xargs /usr/bin/rm"))
		{
			if(!destructive.Contains("xargs rm"))
			{
				destructive.Add("xargs rm");
			}
		}

		return destructive;
	}

	/// <summary>
	/// Extract the file/directory paths that will be deleted by rm commands.
	/// </summary>
	public static List<string> ExtractFilesToDelete(string command)
	{
		List<string> files = [];

		// Split command by shell operators
		string[] segments = command.Split([';', '&', '|', '\n'], StringSplitOptions.RemoveEmptyEntries);

		foreach(string segment in segments)
		{
			string trimmed = segment.Trim();
			if(string.IsNullOrWhiteSpace(trimmed))
			{
				continue;
			}

			// Parse rm, rmdir, unlink, shred commands
			Match rmMatch = RmCommandPattern().Match(trimmed);
			if(!rmMatch.Success)
			{
				continue;
			}

			string args = rmMatch.Groups[2].Value.Trim();
			if(string.IsNullOrEmpty(args))
			{
				continue;
			}

			// Parse the arguments, handling quoted strings and flags
			List<string> tokens = TokenizeShellArgs(args);

			foreach(string token in tokens)
			{
				// Skip flags (anything starting with -)
				if(token.StartsWith('-'))
				{
					continue;
				}
				// Skip empty tokens
				if(string.IsNullOrWhiteSpace(token))
				{
					continue;
				}
				// This is a file/directory path
				files.Add(token);
			}
		}

		return files;
	}

	/// <summary>
	/// Tokenize shell arguments, handling quoted strings.
	/// </summary>
	static List<string> TokenizeShellArgs(string args)
	{
		List<string> tokens = [];
		StringBuilder current = new();
		bool inSingleQuote = false;
		bool inDoubleQuote = false;
		bool escaped = false;

		for(int i = 0; i < args.Length; i++)
		{
			char c = args[i];

			if(escaped)
			{
				current.Append(c);
				escaped = false;
				continue;
			}

			if(c == '\\' && !inSingleQuote)
			{
				escaped = true;
				continue;
			}

			if(c == '\'' && !inDoubleQuote)
			{
				inSingleQuote = !inSingleQuote;
				continue;
			}

			if(c == '"' && !inSingleQuote)
			{
				inDoubleQuote = !inDoubleQuote;
				continue;
			}

			if(c == ' ' && !inSingleQuote && !inDoubleQuote)
			{
				if(current.Length > 0)
				{
					tokens.Add(current.ToString());
					current.Clear();
				}
				continue;
			}

			current.Append(c);
		}

		if(current.Length > 0)
		{
			tokens.Add(current.ToString());
		}

		return tokens;
	}
}
