using System.Text;
using System.Text.RegularExpressions;

namespace Cockpit.Features.Permissions;

/// <summary>
/// Utility for extracting executables from shell commands.
/// Based on Cooper's extractExecutables.ts implementation.
/// </summary>
public static class CommandExtractor
{
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
		// First decode Unicode escapes
		string result = Regex.Replace(input, @"\\u([0-9A-Fa-f]{4})", m =>
		{
			int codePoint = Convert.ToInt32(m.Groups[1].Value, 16);
			return ((char)codePoint).ToString();
		});
		
		// Then decode common escape sequences
		result = result.Replace("\\n", "\n")
		               .Replace("\\r", "\r")
		               .Replace("\\t", "\t");
		
		return result;
	}

	/// <summary>
	/// Process command substitutions $(...)  - remove the wrapper but keep content for pipeline commands
	/// Standalone utility commands like basename/dirname in nested substitutions are removed
	/// </summary>
	static string ProcessCommandSubstitutions(string input)
	{
		StringBuilder result = new();
		int i = 0;
		
		while(i < input.Length)
		{
			// Check for $(...) command substitution
			if(i < input.Length - 1 && input[i] == '$' && input[i + 1] == '(')
			{
				// Skip the $(
				i += 2;
				int start = i;
				int depth = 1;
				
				// Find the matching )
				while(i < input.Length && depth > 0)
				{
					if(input[i] == '(')
					{
						depth++;
					}
					else if(input[i] == ')')
					{
						depth--;
						if(depth == 0)
						{
							// Extract the content between $( and )
							string content = input.Substring(start, i - start);
							
							// Check if this is a simple utility command that wraps another substitution
							// Pattern: basename $(...)  or dirname $(...)
							if(Regex.IsMatch(content.Trim(), @"^(basename|dirname)\s+\$\("))
							{
								// Skip this substitution entirely - don't extract basename/dirname
								i++;
								continue;
							}
							
							// Otherwise, keep the content (remove the $() wrapper)
							result.Append(' '); // Add space as separator
							result.Append(content);
							result.Append(' ');
							i++;
							continue;
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
		// For these, we want to keep the scriptblock CONTENT to extract commands from it
		HashSet<string> cmdletsWithScriptblocksKeepContent = new(StringComparer.OrdinalIgnoreCase)
		{
			"ForEach-Object", "Where-Object", "Measure-Command"
		};
		
		// Cmdlets where we should remove the entire scriptblock
		HashSet<string> cmdletsWithScriptblocksRemoveContent = new(StringComparer.OrdinalIgnoreCase)
		{
			"Invoke-Command"
		};
		
		StringBuilder result = new();
		int i = 0;
		
		while(i < input.Length)
		{
			// Check if we're at a position where a scriptblock parameter might start
			if(i < input.Length && input[i] == '{')
			{
				// Look back to see if this is a scriptblock parameter
				int lookback = i - 1;
				while(lookback >= 0 && char.IsWhiteSpace(input[lookback]))
				{
					lookback--;
				}
				
				// Check if preceded by comma or opening paren (scriptblock parameter)
				bool isScriptblockParam = lookback >= 0 && (input[lookback] == ',' || input[lookback] == '(');
				bool keepContent = false;
				
				// Also check if preceded by a cmdlet name that takes scriptblocks
				if(!isScriptblockParam && lookback >= 0)
				{
					// Extract the word before the brace
					int wordEnd = lookback + 1;
					int wordStart = lookback;
					while(wordStart > 0 && (char.IsLetterOrDigit(input[wordStart - 1]) || input[wordStart - 1] == '-'))
					{
						wordStart--;
					}
					
					if(wordStart < wordEnd)
					{
						string word = input.Substring(wordStart, wordEnd - wordStart);
						if(cmdletsWithScriptblocksKeepContent.Contains(word))
						{
							// This is a cmdlet with scriptblock - remove the cmdlet name and braces but keep content
							isScriptblockParam = true;
							keepContent = true;
							// Remove the cmdlet name from result
							result.Length = Math.Max(0, result.Length - (wordEnd - wordStart));
						}
						else if(cmdletsWithScriptblocksRemoveContent.Contains(word))
						{
							// Remove cmdlet name and entire scriptblock
							isScriptblockParam = true;
							keepContent = false;
							result.Length = Math.Max(0, result.Length - (wordEnd - wordStart));
						}
					}
				}
				
				if(isScriptblockParam)
				{
					// This is a scriptblock parameter
					int depth = 1;
					i++; // skip opening brace
					
					if(keepContent)
					{
						// Keep the scriptblock content but remove the braces
						while(i < input.Length && depth > 0)
						{
							if(input[i] == '{')
							{
								depth++;
							}
							else if(input[i] == '}')
							{
								depth--;
								if(depth == 0)
								{
									// Skip the closing brace
									i++;
									break;
								}
							}
							
							// Keep the content
							result.Append(input[i]);
							i++;
						}
					}
					else
					{
						// Remove the entire scriptblock
						while(i < input.Length && depth > 0)
						{
							if(input[i] == '{')
							{
								depth++;
							}
							else if(input[i] == '}')
							{
								depth--;
							}
							i++;
						}
					}
					continue;
				}
			}
			
			// Not a scriptblock, keep the character
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
		List<string> executables = [];

		// Decode Unicode escape sequences first (e.g., \u0027 -> ')
		command = DecodeUnicodeEscapes(command);

		// Remove heredocs first (<<'MARKER' ... MARKER or <<MARKER ... MARKER)
		string cleaned = Regex.Replace(command, @"<<\s*['""']?(\w+)['""']?[\s\S]*?\n\1\s*(?=\n|$)", "");
		cleaned = Regex.Replace(cleaned, @"<<\s*['""']?\w+['""']?[\s\S]*$", "");

		// Remove string literals first to avoid false positives
		cleaned = Regex.Replace(cleaned, @"""[^""]*""", @"""""");
		cleaned = Regex.Replace(cleaned, @"'[^']*'", @"''");
		cleaned = Regex.Replace(cleaned, @"`[^`]*`", @"``");

		// Process command substitutions like $(...)
		cleaned = ProcessCommandSubstitutions(cleaned);

		// Then remove PowerShell scriptblocks { ... } passed as parameters
		cleaned = RemoveScriptblocks(cleaned);

		// Expand parentheses to extract commands from subexpressions like (Get-Item ...)
		cleaned = ExpandParentheses(cleaned);

		// Remove shell comments (# to end of line)
		cleaned = Regex.Replace(cleaned, @"#[^\n]*", "");

		// Remove shell redirections like 2>&1, >&2, 2>/dev/null, etc.
		cleaned = Regex.Replace(cleaned, @"\d*>&?\d+", "");
		cleaned = Regex.Replace(cleaned, @"\d+>>\S+", "");
		cleaned = Regex.Replace(cleaned, @"\d+>\S+", "");

		// Split on shell operators, separators, and newlines
		string[] segments = cleaned.Split([';', '&', '|', '\n'], StringSplitOptions.RemoveEmptyEntries);

		foreach(string segment in segments)
		{
			string trimmed = segment.Trim();
			if(string.IsNullOrWhiteSpace(trimmed))
			{
				continue;
			}

			// Skip if it looks like a heredoc marker line
			if(Regex.IsMatch(trimmed, @"^[A-Z]+$"))
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

				// Skip environment variable assignments
				if(part.Contains('=') && !part.StartsWith('-'))
				{
					continue;
				}

				// Skip flags - anything starting with hyphen
				if(part.StartsWith('-'))
				{
					// Check if this is a flag that takes a value
					if(flagsWithValues.Contains(part))
					{
						skipNextAsFlagValue = true;
					}
					continue;
				}

				// Skip common prefixes
				if(prefixes.Contains(part))
				{
					continue;
				}

				// Skip shell builtins
				if(shellBuiltinsToSkip.Contains(part))
				{
					continue;
				}

				// Skip shell keywords
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

				// Skip the loop variable after 'for' or 'select'
				if(skipNextAsLoopVar)
				{
					skipNextAsLoopVar = false;
					continue;
				}

				// Skip empty or punctuation
				if(string.IsNullOrEmpty(part) || Regex.IsMatch(part, @"^[<>|&;()]+$"))
				{
					continue;
				}

				// Skip redirection targets
				if(part.StartsWith('>') || part.StartsWith('<'))
				{
					continue;
				}

				// Found potential executable - remove path prefix
				string exec = part.Contains('/') ? part[(part.LastIndexOf('/') + 1)..] : part;
				exec = exec.Contains('\\') ? exec[(exec.LastIndexOf('\\') + 1)..] : exec;

				// Validate it looks like a command (alphanumeric, dashes, underscores)
				// Must contain at least one letter (not just numbers) to be a valid command
				if(!string.IsNullOrEmpty(exec) && Regex.IsMatch(exec, @"^[a-zA-Z0-9_\-\.]+$") && Regex.IsMatch(exec, @"[a-zA-Z]"))
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
								// Skip value after flag
								if(skipNextSubValue)
								{
									skipNextSubValue = false;
									continue;
								}
								// Skip flags
								if(nextPart.StartsWith('-'))
								{
									if(flagsWithValues.Contains(nextPart))
									{
										skipNextSubValue = true;
									}
									continue;
								}
								// Skip environment variables
								if(nextPart.Contains('='))
								{
									continue;
								}
								// Found potential subcommand
								if(Regex.IsMatch(nextPart, @"^[a-zA-Z0-9_\-]+$"))
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
				string execId = subcommand != null ? $"{foundExec} {subcommand}" : foundExec;
				if(!executables.Contains(execId))
				{
					executables.Add(execId);
				}
			}
		}

		return executables;
	}

	/// <summary>
	/// Filter out navigation/informational commands, returning only commands that require permissions.
	/// Useful for permission UI to show only the "meaningful" operations.
	/// </summary>
	public static List<string> ExtractMeaningfulExecutables(string command)
	{
		List<string> all = ExtractExecutables(command);
		return all.Where(cmd => !navigationCommands.Contains(cmd)).ToList();
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
			Match rmMatch = Regex.Match(trimmed, @"(?:^|(?:sudo|env|nohup|nice|time|command)\s+)*(rm|rmdir|unlink|shred)(?:\s|$)(.*)");
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
