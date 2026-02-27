using System.Text;
using System.Text.RegularExpressions;

namespace Cockpit.Features.Permissions;

/// <summary>
/// Utility for extracting executables from shell commands.
/// Based on Cooper's extractExecutables.ts implementation.
/// </summary>
public static partial class CommandExtractor
{
	// ===============================
	// Generated Regex Patterns
	// ===============================

	/// <summary>Unicode escape sequences like \u0027</summary>
	[GeneratedRegex(@"\\u([0-9A-Fa-f]{4})")]
	private static partial Regex UnicodeEscapePattern();

	/// <summary>Heredoc with closing marker (e.g., &lt;&lt;EOF ... EOF)</summary>
	[GeneratedRegex(@"<<\s*['""']?(\w+)['""']?[\s\S]*?\n\1\s*(?=\n|$)")]
	private static partial Regex HeredocWithMarkerPattern();

	/// <summary>Heredoc without closing marker (end of input)</summary>
	[GeneratedRegex(@"<<\s*['""']?\w+['""']?[\s\S]*$")]
	private static partial Regex HeredocEndPattern();

	/// <summary>String literals (single, double, and backtick quotes)</summary>
	[GeneratedRegex(@"""[^""]*""|'[^']*'|`[^`]*`")]
	private static partial Regex StringLiteralsPattern();

	/// <summary>Shell comments and I/O redirections</summary>
	[GeneratedRegex(@"#[^\n]*|\d*>&?\d+|\d+>>\S+|\d+>\S+")]
	private static partial Regex CommentsAndRedirectionsPattern();

	/// <summary>All-uppercase heredoc markers</summary>
	[GeneratedRegex(@"^[A-Z]+$")]
	private static partial Regex HeredocMarkerPattern();

	/// <summary>Shell punctuation and operators</summary>
	[GeneratedRegex(@"^[<>|&;()]+$")]
	private static partial Regex PunctuationPattern();

	/// <summary>Valid command name pattern</summary>
	[GeneratedRegex(@"^(?=.*[a-zA-Z])[a-zA-Z0-9_\-\.]+$")]
	private static partial Regex CommandValidationPattern();

	/// <summary>Valid subcommand pattern</summary>
	[GeneratedRegex(@"^[a-zA-Z0-9_\-]+$")]
	private static partial Regex SubcommandValidationPattern();

	/// <summary>Utility commands like basename/dirname in command substitutions</summary>
	[GeneratedRegex(@"^(basename|dirname)\s+\$\(")]
	private static partial Regex UtilityCommandPattern();

	/// <summary>rm/rmdir/unlink/shred commands with optional prefixes</summary>
	[GeneratedRegex(@"(?:^|(?:sudo|env|nohup|nice|time|command)\s+)*(rm|rmdir|unlink|shred)(?:\s|$)(.*)")]
	private static partial Regex RmCommandPattern();

	// ===============================
	// Command Classification Sets
	// ===============================

	// Commands that should include their subcommand for granular permission control
	static readonly HashSet<string> subcommandExecutables = ["git", "npm", "yarn", "pnpm", "docker", "kubectl", "gh", "dotnet", "cargo"];

	// Shell builtins that are not real executables and should be skipped
	static readonly HashSet<string> shellBuiltinsToSkip = ["true", "false"];

	// Shell keywords that are part of control flow syntax, not executables
	// Includes both bash/sh and PowerShell keywords
	static readonly HashSet<string> shellKeywordsToSkip = new(StringComparer.OrdinalIgnoreCase)
	{
		// bash/sh control flow
		"for", "in", "do", "done", "while", "until", "if", "then", "else", "elif", "fi", "case", "esac", "select",
		// PowerShell control flow keywords that can appear as tokens
		"foreach", "elseif", "break", "continue", "return", "exit", "throw",
		"begin", "process", "end", "filter", "function", "param",
		// Things that are clearly not commands (common false-positive tokens leaked from text)
		"from", "to", "old", "new", "url",
	};

	// Flags that are commonly followed by values that might look like commands
	// Must be comprehensive to avoid false positives where flag values are treated as commands
	static readonly HashSet<string> flagsWithValues =
	[
		// HTTP/curl flags
		"-X", "--request",      // HTTP method (GET, POST, PUT, etc.)
		"-H", "--header",       // HTTP header
		"-d", "--data",         // POST data
		"-o", "--output",       // Output file/format
		"-u", "--user",         // Username
		"-T", "--upload-file",  // Upload file
		"-A", "--user-agent",   // User agent
		"-e", "--referer",      // Referer
		"-b", "--cookie",       // Cookie
		"-c", "--cookie-jar",   // Cookie jar file
		"-F", "--form",         // Form data
		// Azure CLI flags
		"--name", "--resource-group", "-g", "--subscription", "-s",
		"--location", "-l", "--query", "--sku", "--image", "--size",
		// Docker flags
		"--network", "--volume", "-v", "--env", "--publish", "-p",
		"--workdir", "-w", "--entrypoint",
		// Git flags
		"-m", "--message", "-b", "--branch", "-C",
		// kubectl flags
		"-n", "--namespace", "-f", "--filename", "--context",
		// General flags
		"-t", "--tag", "-i", "--input", "--type", "--format", "--filter"
	];

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

	// Commands that are always safe and should never require a permission prompt.
	// These are read-only, informational or navigation commands with no side-effects.
	// NOTE: entries must match what CommandExtractor.ExtractExecutables returns (executable + subcommand),
	// not the raw shell string.
	static readonly HashSet<string> safeCommands = new(StringComparer.OrdinalIgnoreCase)
	{
		// Navigation
		"cd", "pushd", "popd", "pwd",
		"Set-Location", "Get-Location",
		// Directory listing (find excluded — can be destructive with -delete/-exec rm)
		"ls", "dir",
		// Output / inspection
		"echo", "printf", "cat", "type", "head", "tail", "less", "more",
		"Out-Host", "Out-Null", "Out-String",
		// Search / filter
		"grep", "rg", "ag", "Select-String", "Where-Object",
		// Pipeline transforms (read-only — no I/O side effects)
		"Select-Object", "Sort-Object", "Group-Object", "Format-List", "Format-Table",
		"ForEach-Object", "ConvertFrom-Json", "ConvertTo-Json", "Join-String",
		// File / path inspection
		"Get-Content", "Get-Item", "Get-ChildItem", "Test-Path",
		// Process / system inspection
		"Get-Process",
		// Command lookup
		"which", "where", "whereis",
		// Environment
		"env", "printenv",
		// Git read-only subcommands (extracted as "git <subcommand>" by CommandExtractor)
		"git status", "git log", "git diff", "git branch", "git show",
		"git remote", "git tag", "git describe",
		// npm info subcommands
		"npm list", "npm ls", "npm outdated",
		// dotnet info subcommand
		"dotnet list",
		// Misc read-only
		"uname", "hostname", "whoami", "id", "date",
	};

	// PowerShell cmdlets where we keep scriptblock content
	static readonly HashSet<string> cmdletsKeepContent = new(StringComparer.OrdinalIgnoreCase)
	{
		"ForEach-Object", "Measure-Command"
	};

	// PowerShell cmdlets where we remove entire scriptblock
	static readonly HashSet<string> cmdletsRemoveContent = new(StringComparer.OrdinalIgnoreCase)
	{
		"Invoke-Command", "Where-Object"
	};

	/// <summary>
	/// Decode Unicode escape sequences in a string (e.g., \u0026 -&gt; &amp;, \u003E -&gt; &gt;).
	/// Also decodes \n -&gt; newline so multi-command strings like "echo hello\nls" are split correctly.
	/// </summary>
	static string DecodeUnicodeEscapes(string input)
	{
		string decoded = UnicodeEscapePattern().Replace(input, m => ((char)Convert.ToInt32(m.Groups[1].Value, 16)).ToString());
		return decoded.Replace("\\n", "\n");
	}

	/// <summary>
	/// Split a shell command string by operators (;, &amp;, |, newlines) while respecting
	/// single-quoted and double-quoted strings. Characters inside quotes are never treated
	/// as operators, preventing PS regex patterns like '...;...' from being incorrectly split.
	/// </summary>
	static IEnumerable<string> SplitByShellOperators(string input)
	{
		StringBuilder current = new(input.Length);
		bool inSingleQuote = false;
		bool inDoubleQuote = false;
		char prev = '\0';

		for(int i = 0; i < input.Length; i++)
		{
			char c = input[i];

			if(!inDoubleQuote && c == '\'')
			{
				if(inSingleQuote)
				{
					// Always close single quotes
					inSingleQuote = false;
				}
				else if(!char.IsLetterOrDigit(prev) && prev != '_')
				{
					// Only open single quotes at the start of a word (not in apostrophes like it's)
					inSingleQuote = true;
				}
			}
			else if(!inSingleQuote && c == '"' && prev != '`')
			{
				inDoubleQuote = !inDoubleQuote;
			}

			if(!inSingleQuote && !inDoubleQuote && (c == ';' || c == '&' || c == '|' || c == '\n' || c == '\r'))
			{
				if(current.Length > 0)
				{
					yield return current.ToString();
					current.Clear();
				}
			}
			else
			{
				current.Append(c);
			}

			prev = c;
		}

		if(current.Length > 0)
		{
			yield return current.ToString();
		}
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

						// Skip string literals so { and } inside them don't affect depth
						if(c == '"' || c == '\'')
						{
							char quote = c;
							char prevC = '\0';
							if(keepContent)
							{
								result.Append(c);
							}

							i++;
							while(i < input.Length)
							{
								char sc = input[i];
								// PS double-quoted strings use backtick as escape; single-quoted need no escape
								bool isEscaped = quote == '"' && prevC == '`';
								if(sc == quote && !isEscaped)
								{
									if(keepContent)
									{
										result.Append(sc);
									}

									i++;
									break;
								}
								if(keepContent)
								{
									result.Append(sc);
								}

								prevC = sc;
								i++;
							}
							continue;
						}

						if(c == '{')
						{
							depth++;
						}
						else if(c == '}' && --depth == 0)
						{
							i++;
							break;
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

		// Phase 1: transformations that need the full multi-line text.
		// RemoveScriptblocks MUST run here — multi-line scriptblocks like
		// `[regex]::Replace($x, $pat, { ... })` span multiple lines and would
		// leak their content as false-positive segments if we split first.
		string preprocessed = DecodeUnicodeEscapes(command);
		preprocessed = HeredocWithMarkerPattern().Replace(preprocessed, "");
		preprocessed = HeredocEndPattern().Replace(preprocessed, "");
		preprocessed = RemoveScriptblocks(preprocessed);

		// Phase 2: split and process each segment.
		// SplitByShellOperators respects quoted strings — ';' inside '...' is not an operator.
		foreach(string segment in SplitByShellOperators(preprocessed))
		{
			string trimmed = segment.Trim();
			if(string.IsNullOrWhiteSpace(trimmed) || HeredocMarkerPattern().IsMatch(trimmed))
			{
				continue;
			}

			// Launcher detection on the pre-processed segment (string literals still intact).
			// Must happen before CleanSegment so `powershell -c "..."` keeps its quoted inner command.
			string[] rawParts = trimmed.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
			if(rawParts.Length == 0)
			{
				continue;
			}

			string launcher = rawParts[0].ToLowerInvariant();
			if((launcher == "cmd" || launcher == "cmd.exe") && rawParts.Length > 2
				&& rawParts[1].Equals("/c", StringComparison.OrdinalIgnoreCase))
			{
				string inner = StripOuterQuotes(string.Join(' ', rawParts[2..]));
				foreach(string innerExec in ExtractExecutables(inner))
				{
					executables.Add(innerExec);
				}

				continue;
			}

			if((launcher == "powershell" || launcher == "powershell.exe") && rawParts.Length > 2)
			{
				int idx = Array.FindIndex(rawParts, p =>
					p.Equals("-Command", StringComparison.OrdinalIgnoreCase)
					|| p.Equals("-c", StringComparison.OrdinalIgnoreCase));
				if(idx >= 0 && idx < rawParts.Length - 1)
				{
					string inner = StripOuterQuotes(string.Join(' ', rawParts[(idx + 1)..]));
					foreach(string innerExec in ExtractExecutables(inner))
					{
						executables.Add(innerExec);
					}

					continue;
				}
			}

			// Standard path: apply remaining per-segment cleaning and extract.
			string cleaned = CleanSegment(trimmed);
			if(string.IsNullOrWhiteSpace(cleaned))
			{
				continue;
			}

			string[] parts = cleaned.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
			if(TryExtractExecutable(parts, out string? exec, out string? subcommand) && exec is not null)
			{
				executables.Add(subcommand is not null ? $"{exec} {subcommand}" : exec);
			}
		}

		return [.. executables];
	}

	/// <summary>
	/// Strip matching outer single or double quotes from a string (used to unwrap launcher args).
	/// </summary>
	static string StripOuterQuotes(string s)
	{
		s = s.Trim();
		if(s.Length >= 2 && ((s[0] == '"' && s[^1] == '"') || (s[0] == '\'' && s[^1] == '\'')))
		{
			return s[1..^1];
		}

		return s;
	}

	/// <summary>
	/// Clean command by removing strings, substitutions, comments, etc.
	/// Used when the full command is passed as a single string (e.g. recursive inner-command calls).
	/// </summary>
	static string CleanCommand(string command)
	{
		command = DecodeUnicodeEscapes(command);
		command = HeredocWithMarkerPattern().Replace(command, "");
		command = HeredocEndPattern().Replace(command, "");
		command = RemoveScriptblocks(command);
		return CleanSegment(command);
	}

	/// <summary>
	/// Apply per-segment cleaning (string literals, substitutions, parentheses, redirections).
	/// Assumes heredocs and scriptblocks have already been removed from the text.
	/// </summary>
	static string CleanSegment(string segment)
	{
		segment = StringLiteralsPattern().Replace(segment, m => new string(m.Value[0], 2));
		segment = ProcessCommandSubstitutions(segment);
		segment = ExpandParentheses(segment);
		return CommentsAndRedirectionsPattern().Replace(segment, "");
	}

	/// <summary>
	/// Try to extract the first executable and optional subcommand from parts.
	/// </summary>
	static bool TryExtractExecutable(string[] parts, out string? exec, out string? subcommand)
	{
		exec = null;
		subcommand = null;

		bool skipNextAsLoopVar = false;
		bool inForValueList = false;
		bool skipNextAsFlagValue = false;

		for(int i = 0; i < parts.Length; i++)
		{
			string part = parts[i];

			// State-based skipping
			if(inForValueList)
			{
				if(part == "do")
				{
					inForValueList = false;
				}

				continue;
			}
			if(skipNextAsFlagValue)
			{
				skipNextAsFlagValue = false;
				continue;
			}
			if(skipNextAsLoopVar)
			{
				skipNextAsLoopVar = false;
				continue;
			}

			// Quick rejection checks
			if(ShouldSkipPart(part, ref skipNextAsFlagValue))
			{
				continue;
			}

			// Handle shell keywords
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

			// Skip punctuation-only
			if(PunctuationPattern().IsMatch(part))
			{
				continue;
			}

			// Extract and validate executable
			exec = GetExecutableFromPath(part);
			if(string.IsNullOrEmpty(exec) || !CommandValidationPattern().IsMatch(exec))
			{
				continue;
			}

			// Find subcommand if needed
			if(subcommandExecutables.Contains(exec))
			{
				subcommand = FindSubcommand(parts, i + 1);
			}
			return true;
		}

		return false;
	}

	/// <summary>
	/// Check if a part should be skipped (env vars, flags, redirects, prefixes, builtins).
	/// </summary>
	static bool ShouldSkipPart(string part, ref bool skipNextAsFlagValue)
	{
		if(string.IsNullOrEmpty(part))
		{
			return true;
		}

		if(part.Contains('=') && !part.StartsWith('-'))
		{
			return true;
		}

		if(part.StartsWith('>') || part.StartsWith('<'))
		{
			return true;
		}

		if(part.StartsWith('-'))
		{
			if(flagsWithValues.Contains(part))
			{
				skipNextAsFlagValue = true;
			}

			return true;
		}

		return prefixes.Contains(part) || shellBuiltinsToSkip.Contains(part);
	}

	/// <summary>
	/// Extract executable name from a path (handles / and \\ separators).
	/// </summary>
	static string GetExecutableFromPath(string part)
	{
		int lastSep = Math.Max(part.LastIndexOf('/'), part.LastIndexOf('\\'));
		return lastSep >= 0 ? part[(lastSep + 1)..] : part;
	}

	/// <summary>
	/// Find the first valid subcommand in parts starting from startIndex.
	/// </summary>
	static string? FindSubcommand(string[] parts, int startIndex)
	{
		bool skipNext = false;
		for(int j = startIndex; j < parts.Length; j++)
		{
			string part = parts[j];
			if(skipNext)
			{
				skipNext = false;
				continue;
			}

			if(part.StartsWith('-'))
			{
				if(flagsWithValues.Contains(part))
				{
					skipNext = true;
				}

				continue;
			}
			if(part.Contains('='))
			{
				continue;
			}

			if(SubcommandValidationPattern().IsMatch(part))
			{
				return part;
			}

			break;
		}
		return null;
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
	/// Returns true if all commands extracted from the given shell command are considered
	/// safe (read-only / informational) and can be auto-approved without user interaction.
	/// </summary>
	public static bool AreAllCommandsSafe(List<string> commands)
	{
		return commands.Count > 0 && commands.All(cmd => safeCommands.Contains(cmd));
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
		// Check extracted executables first
		if(ExtractExecutables(command).Any(IsDestructiveExecutable))
		{
			return true;
		}

		// Check for special destructive patterns
		ReadOnlySpan<char> lower = command.ToLowerInvariant().AsSpan();

		// git push with --force or -f
		if(lower.Contains("git push", StringComparison.Ordinal) &&
		   (lower.Contains("--force", StringComparison.Ordinal) || lower.Contains(" -f", StringComparison.Ordinal)))
		{
			return true;
		}

		// find with -delete or -exec rm
		if(lower.Contains("find ", StringComparison.Ordinal) &&
		   (lower.Contains("-delete", StringComparison.Ordinal) ||
			lower.Contains("-exec rm", StringComparison.Ordinal) ||
			lower.Contains("-exec /bin/rm", StringComparison.Ordinal) ||
			lower.Contains("-exec /usr/bin/rm", StringComparison.Ordinal)))
		{
			return true;
		}

		// xargs with rm
		return lower.Contains("xargs rm", StringComparison.Ordinal) ||
			   lower.Contains("xargs /bin/rm", StringComparison.Ordinal) ||
			   lower.Contains("xargs /usr/bin/rm", StringComparison.Ordinal);
	}

	/// <summary>
	/// Get a list of destructive executables found in a command.
	/// </summary>
	public static List<string> GetDestructiveExecutables(string command)
	{
		List<string> destructive = [.. ExtractExecutables(command).Where(IsDestructiveExecutable)];

		ReadOnlySpan<char> lower = command.ToLowerInvariant().AsSpan();

		// Special case: find with -delete/-exec rm
		if(lower.Contains("find ", StringComparison.Ordinal) &&
		   (lower.Contains("-delete", StringComparison.Ordinal) ||
			lower.Contains("-exec rm", StringComparison.Ordinal) ||
			lower.Contains("-exec /bin/rm", StringComparison.Ordinal) ||
			lower.Contains("-exec /usr/bin/rm", StringComparison.Ordinal)) &&
		   !destructive.Contains("find -delete"))
		{
			destructive.Add("find -delete");
		}

		// Special case: xargs with rm
		if((lower.Contains("xargs rm", StringComparison.Ordinal) ||
			lower.Contains("xargs /bin/rm", StringComparison.Ordinal) ||
			lower.Contains("xargs /usr/bin/rm", StringComparison.Ordinal)) &&
		   !destructive.Contains("xargs rm"))
		{
			destructive.Add("xargs rm");
		}

		return destructive;
	}

	/// <summary>
	/// Extract the file/directory paths that will be deleted by rm commands.
	/// </summary>
	public static List<string> ExtractFilesToDelete(string command)
	{
		List<string> files = [];

		foreach(string segment in command.Split([';', '&', '|', '\n'], StringSplitOptions.RemoveEmptyEntries))
		{
			string trimmed = segment.Trim();
			if(string.IsNullOrWhiteSpace(trimmed))
			{
				continue;
			}

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
			foreach(string token in TokenizeShellArgs(args))
			{
				if(!token.StartsWith('-') && !string.IsNullOrWhiteSpace(token))
				{
					files.Add(token);
				}
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
