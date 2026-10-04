using System.Collections.Immutable;
using Cockpit.Features.Git;
using Cockpit.Features.Git.Models;
using Cockpit.Features.Instructions;
using Cockpit.Features.Mcp;
using Cockpit.Features.Permissions;
using Cockpit.Features.Sessions;
using Cockpit.Features.Sessions.Models;
using Cockpit.Features.Skills;
using Cockpit.Features.UIState;
using Cockpit.Features.VSCode;
using Cockpit.Tools.GitChanges.Sources;
using Cockpit.Tools.SessionContext.Sources;
using Cockpit.Utilities;
using McpServerStatus = GitHub.Copilot.McpServerStatus;

namespace Cockpit.Features.Workspace;

sealed class SessionContextToolSource(
	SessionListFeature sessions,
	SessionFeature sessionFeature,
	McpFeature mcpFeature,
	SkillsFeature skillsFeature,
	GlobalPermissionFeature globalPermissions,
	SessionPermissionFeature sessionPermissions,
	VsCodeFeature vsCodeFeature,
	IUIStateFeature uiState) : ISessionContextToolSource
{
	long _generation;

	public ValueTask<SessionContextSnapshot?> GetSnapshotAsync(string runtimeSessionKey, long identityEpoch, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		SessionModel? session = sessions.Sessions.FirstOrDefault(item => item.Id == runtimeSessionKey);
		if(session is null)
		{
			return ValueTask.FromResult<SessionContextSnapshot?>(null);
		}

		SessionContext context = session.Context;
		ImmutableArray<SessionContextSectionSnapshot> sections =
		[
			Section("directory", "Directory", DirectoryItems(context)),
			Section("git", "Git Branch", string.IsNullOrWhiteSpace(context.GitRoot) ? [] :
				[Item("branch", context.Branch ?? "No branch", kind: "git")]),
			Section("agents", $"Agents ({context.Agents.Count})", context.Agents.Select(agent =>
			{
				MarkdownDocument document = ReadMarkdownDocument(agent.FilePath);
				return Item(agent.Name, agent.DisplayLabel, agent.Description, "agent", path: agent.FilePath,
					content: document.Body, badge: agent.Source.ToString().ToLowerInvariant(),
					selected: context.SelectedAgent?.Name == agent.Name && context.SelectedAgent?.Source == agent.Source,
					properties: AgentProperties(agent, document.Frontmatter));
			})),
			Section("instructions", $"Instructions ({context.Instructions.Count})", context.Instructions.Select((instruction, index) =>
			{
				string path = ResolvePath(instruction.SourcePath, context.GitRoot ?? context.CurrentWorkingDirectory);
				return Item(string.IsNullOrWhiteSpace(instruction.Id) ? $"instruction-{index}" : instruction.Id,
					InstructionsFeature.GetDisplayLabel(instruction.Label), instruction.Description, "instruction", path: path,
					content: instruction.Content, properties: Properties(
						("Location", instruction.Location.ToString()),
						("Apply To", instruction.ApplyTo is null ? null : string.Join(", ", instruction.ApplyTo)),
						("Description", instruction.Description)));
			})),
			Section("mcp", $"MCP Servers ({context.McpServers.Count})", context.McpServers.Select(server =>
				Item(server.Name, server.Name, server.Error, "mcp", status: McpFeature.GetStatusDisplayString(server.Status),
					enabled: server.Status != McpServerStatus.Disabled, toggle: true,
					properties: Properties(("Source", server.Source?.ToString()), ("Status", McpFeature.GetStatusDisplayString(server.Status))))), true),
			Section("skills", $"Skills ({context.Skills.Count})", context.Skills.Select(skill =>
				Item(skill.Name, skill.Name, skill.Description, "skill", path: skill.Path, content: ReadMarkdownDocument(skill.Path).Body,
					enabled: skill.Enabled, toggle: true, properties: Properties(
						("Source", skill.Source.ToString()), ("Enabled", skill.Enabled ? "Yes" : "No"),
						("User Invocable", skill.UserInvocable ? "Yes" : "No"), ("Description", skill.Description)))), true),
			Section("plugins", $"Plugins ({context.Plugins.Count})", context.Plugins.Select(plugin =>
				Item(plugin.Name, plugin.Name, kind: "plugin", enabled: plugin.Enabled, badge: plugin.Enabled ? null : "off",
					properties: Properties(("Marketplace", plugin.Marketplace), ("Version", plugin.Version?.ToString()),
						("Enabled", plugin.Enabled ? "Yes" : "No"))))),
			Section("commands", $"Allowed Commands ({globalPermissions.GetAll().Count + sessionPermissions.GetAll(runtimeSessionKey).Count})",
				sessionPermissions.GetAll(runtimeSessionKey).Select(command => Item($"session:{command}", command, kind: "session-command", badge: "session"))
					.Concat(globalPermissions.GetAll().Select(command => Item($"global:{command}", command, kind: "global-command", badge: "global"))))
		];
		TokenUsageInfoModel? usage = session.TokenUsageInfo;
		return ValueTask.FromResult<SessionContextSnapshot?>(new SessionContextSnapshot
		{
			RuntimeSessionKey = runtimeSessionKey,
			IdentityEpoch = identityEpoch,
			Generation = Interlocked.Increment(ref _generation),
			CapturedAt = DateTimeOffset.UtcNow,
			CurrentWorkingDirectory = context.CurrentWorkingDirectory,
			WorkspacePath = context.WorkspacePath,
			Repository = context.Repository,
			Branch = context.Branch,
			CanOpenInVsCode = vsCodeFeature.IsAvailable,
			IsCompacting = session.IsCompacting,
			CanCompact = !session.IsCompacting && session.Lifecycle.AgentRunState != AgentRunStateEnum.Running,
			TokenUsage = usage is null ? null : new(usage.CurrentTokens, usage.TokenLimit,
				usage.TokenLimit <= 0 ? null : usage.CurrentTokens / usage.TokenLimit * 100,
				usage.SystemTokens, usage.ConversationTokens, usage.ToolDefinitionsTokens, usage.MessagesLength),
			Sections = sections
		});
	}

	public IDisposable Subscribe(string runtimeSessionKey, long identityEpoch, Action<SessionContextSourceChange> onChanged)
	{
		void Handler(SessionStateChange change)
		{
			if(change.SessionId is null || change.SessionId == runtimeSessionKey)
			{
				onChanged(new(runtimeSessionKey, identityEpoch, Interlocked.Increment(ref _generation)));
			}
		}
		sessions.OnSessionStateChanged += Handler;
		void GlobalPermissionsChanged() => onChanged(new(runtimeSessionKey, identityEpoch, Interlocked.Increment(ref _generation)));
		globalPermissions.OnPermissionsChanged += GlobalPermissionsChanged;
		return new CallbackDisposable(() =>
		{
			sessions.OnSessionStateChanged -= Handler;
			globalPermissions.OnPermissionsChanged -= GlobalPermissionsChanged;
		});
	}

	public async ValueTask RefreshSectionAsync(string runtimeSessionKey, long identityEpoch, string sectionId, CancellationToken cancellationToken)
	{
		if(sectionId == "mcp") await mcpFeature.ReloadAsync(runtimeSessionKey, cancellationToken);
		else if(sectionId == "skills") await skillsFeature.ReloadAsync(runtimeSessionKey, cancellationToken);
	}

	public async ValueTask<bool> AddItemToMessageAsync(string runtimeSessionKey, long identityEpoch, string sectionId, string itemKey, CancellationToken cancellationToken)
	{
		SessionContextSnapshot? snapshot = await GetSnapshotAsync(runtimeSessionKey, identityEpoch, cancellationToken);
		SessionContextItemSnapshot? item = snapshot?.Sections.FirstOrDefault(section => section.SectionId == sectionId)?.Items.FirstOrDefault(value => value.Key == itemKey);
		if(item is null || sessions.CurrentSession?.Id != runtimeSessionKey)
		{
			return false;
		}
		uiState.AppendChatInput(item.Detail is null ? item.Label : $"{item.Label}: {item.Detail}");
		return true;
	}

	public async ValueTask<SessionContextCommandResult> ExecuteAsync(string runtimeSessionKey, long identityEpoch,
		SessionContextCommandRequest request, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		SessionModel? session = sessions.Sessions.FirstOrDefault(item => item.Id == runtimeSessionKey);
		if(session is null) return SessionContextCommandResult.Failure("The session is no longer available.");
		try
		{
			switch(request.Command)
			{
				case SessionContextCommand.OpenPath:
					if(string.IsNullOrWhiteSpace(request.Argument)) return SessionContextCommandResult.Failure("No path was provided.");
					System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(request.Argument) { UseShellExecute = true });
					break;
				case SessionContextCommand.OpenInVsCode:
					if(string.IsNullOrWhiteSpace(request.Argument) || !vsCodeFeature.IsAvailable) return SessionContextCommandResult.Failure("VS Code is not available.");
					vsCodeFeature.OpenPathInVsCode(request.Argument);
					break;
				case SessionContextCommand.RevealItem:
					FileUtil.RevealFile(request.Argument);
					break;
				case SessionContextCommand.ToggleItem when request.SectionId == "mcp":
				{
					var server = session.Context.McpServers.FirstOrDefault(item => item.Name == request.ItemKey);
					if(server is null) return SessionContextCommandResult.Failure("The MCP server is no longer available.");
					if(server.Status == McpServerStatus.Disabled) await mcpFeature.EnableServerAsync(runtimeSessionKey, server.Name, cancellationToken);
					else await mcpFeature.DisableServerAsync(runtimeSessionKey, server.Name, cancellationToken);
					break;
				}
				case SessionContextCommand.ToggleItem when request.SectionId == "skills":
				{
					var skill = session.Context.Skills.FirstOrDefault(item => item.Name == request.ItemKey);
					if(skill is null) return SessionContextCommandResult.Failure("The skill is no longer available.");
					if(skill.Enabled) await skillsFeature.DisableSkillAsync(runtimeSessionKey, skill.Name, cancellationToken);
					else await skillsFeature.EnableSkillAsync(runtimeSessionKey, skill.Name, cancellationToken);
					break;
				}
				case SessionContextCommand.RefreshSection:
					await RefreshSectionAsync(runtimeSessionKey, identityEpoch, request.SectionId ?? string.Empty, cancellationToken);
					break;
				case SessionContextCommand.RemoveSessionCommand:
					if(request.Argument is not null) sessionPermissions.Remove(runtimeSessionKey, request.Argument);
					break;
				case SessionContextCommand.OpenCommandSettings:
					uiState.OpenSettings(SettingsSectionEnum.Commands);
					break;
				case SessionContextCommand.CompactContext:
					if(sessions.CurrentSession?.Id != runtimeSessionKey) return SessionContextCommandResult.Failure("Select this session before compacting its context.");
					await sessionFeature.CompactContextAsync();
					break;
				default:
					return SessionContextCommandResult.Failure("That action is not supported.");
			}
			return SessionContextCommandResult.Success();
		}
		catch(Exception ex) when(ex is not OperationCanceledException)
		{
			return SessionContextCommandResult.Failure(ex.Message);
		}
	}

	static SessionContextSectionSnapshot Section(string id, string name, IEnumerable<SessionContextItemSnapshot> items, bool canRefresh = false) => new()
	{
		SectionId = id,
		DisplayName = name,
		Items = [.. items],
		CanRefresh = canRefresh
	};

	static SessionContextItemSnapshot Item(string? key, string label, string? detail = null, string? kind = null,
		string? path = null, string? content = null, string? status = null, string? badge = null,
		bool? enabled = null, bool selected = false, bool toggle = false, ImmutableDictionary<string, string>? properties = null) => new()
	{
		Key = string.IsNullOrWhiteSpace(key) ? label : key,
		Label = label,
		Detail = detail,
		Kind = kind,
		Path = path,
		Content = content,
		Status = status,
		Badge = badge,
		Enabled = enabled,
		IsSelected = selected,
		CanToggle = toggle,
		Properties = properties ?? ImmutableDictionary<string, string>.Empty
	};

	static IEnumerable<SessionContextItemSnapshot> DirectoryItems(SessionContext context)
	{
		if(!string.IsNullOrWhiteSpace(context.CurrentWorkingDirectory)) yield return Item("working", context.CurrentWorkingDirectory, "Working directory", "directory", path: context.CurrentWorkingDirectory);
		if(!string.IsNullOrWhiteSpace(context.WorkspacePath)) yield return Item("session", context.WorkspacePath, "Session directory", "directory", path: context.WorkspacePath);
	}

	static ImmutableDictionary<string, string> Properties(params (string Key, string? Value)[] values) => values
		.Where(item => !string.IsNullOrWhiteSpace(item.Value))
		.ToImmutableDictionary(item => item.Key, item => item.Value!, StringComparer.Ordinal);

	static ImmutableDictionary<string, string> AgentProperties(
		Cockpit.Features.Agents.Models.AgentProfile agent,
		ImmutableDictionary<string, string> frontmatter)
	{
		string Read(string key, string fallback) => frontmatter.TryGetValue(key, out string? value) ? value : fallback;
		string ReadEither(string first, string second, string fallback) => frontmatter.TryGetValue(first, out string? value)
			? value : frontmatter.TryGetValue(second, out value) ? value : fallback;
		return new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["Name"] = Read("name", agent.DisplayLabel),
			["Description"] = Read("description", agent.Description ?? "Not specified"),
			["user-invokable"] = FormatBoolean(ReadEither("user-invokable", "user-invocable", agent.UserInvocable ? "true" : "false")),
			["disable-model-invocation"] = FormatBoolean(Read("disable-model-invocation", "false")),
			["licence"] = ReadEither("licence", "license", "Not specified")
		}.ToImmutableDictionary(StringComparer.Ordinal);
	}

	static string FormatBoolean(string value) => bool.TryParse(value, out bool parsed) ? parsed ? "Yes" : "No" : value;

	static string ResolvePath(string? path, string? root) => string.IsNullOrWhiteSpace(path) ? string.Empty :
		!Path.IsPathRooted(path) && !string.IsNullOrWhiteSpace(root) ? Path.GetFullPath(Path.Combine(root, path)) : path;

	static string? ReadText(string? path)
	{
		try { return !string.IsNullOrWhiteSpace(path) && File.Exists(path) ? File.ReadAllText(path) : null; }
		catch { return null; }
	}

	static MarkdownDocument ReadMarkdownDocument(string? path)
	{
		string? raw = ReadText(path);
		if(string.IsNullOrEmpty(raw)) return new(ImmutableDictionary<string, string>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase), string.Empty);
		string content = raw.TrimStart('\uFEFF').ReplaceLineEndings("\n");
		if(!content.StartsWith("---\n", StringComparison.Ordinal))
		{
			return new(ImmutableDictionary<string, string>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase), content.TrimStart('\n'));
		}
		int end = content.IndexOf("\n---", 4, StringComparison.Ordinal);
		if(end <= 0)
		{
			return new(ImmutableDictionary<string, string>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase), content.TrimStart('\n'));
		}

		Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
		string[] lines = content[4..end].Split('\n');
		for(int index = 0; index < lines.Length; index++)
		{
			string line = lines[index];
			int colon = line.IndexOf(':');
			if(colon <= 0 || char.IsWhiteSpace(line[0])) continue;
			string key = line[..colon].Trim();
			string value = line[(colon + 1)..].Trim();
			if(value is ">" or ">-" or "|" or "|-")
			{
				bool folded = value.StartsWith('>');
				List<string> continuation = [];
				while(index + 1 < lines.Length && (lines[index + 1].Length == 0 || char.IsWhiteSpace(lines[index + 1][0])))
				{
					continuation.Add(lines[++index].Trim());
				}
				value = folded ? string.Join(" ", continuation.Where(part => part.Length > 0)) : string.Join("\n", continuation).Trim();
			}
			values[key] = value.Trim('\'', '"');
		}

		int bodyStart = end + 4;
		while(bodyStart < content.Length && content[bodyStart] == '\n') bodyStart++;
		return new(values.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase), content[bodyStart..]);
	}

	readonly record struct MarkdownDocument(ImmutableDictionary<string, string> Frontmatter, string Body);
}

sealed class GitChangesToolSource(
	SessionListFeature sessions,
	GitFeature gitFeature) : IGitChangesToolSource
{
	long _generation;

	public ValueTask<GitAvailability> GetAvailabilityAsync(string runtimeSessionKey, long identityEpoch, string? repositoryRoot, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		SessionModel? session = sessions.Sessions.FirstOrDefault(item => item.Id == runtimeSessionKey);
		string? root = repositoryRoot ?? session?.Context.GitRoot;
		return ValueTask.FromResult(string.IsNullOrWhiteSpace(root)
			? new GitAvailability(false, "This session is not attached to a Git repository.", null)
			: new GitAvailability(true, null, new GitRepositoryIdentity(Path.GetFullPath(root), session?.Context.Repository, session?.Context.Branch)));
	}

	public async ValueTask<GitRepositorySnapshot?> GetSnapshotAsync(string runtimeSessionKey, long identityEpoch, GitRepositoryBinding binding, CancellationToken cancellationToken)
	{
		SessionModel? session = sessions.Sessions.FirstOrDefault(item => item.Id == runtimeSessionKey);
		string? root = binding.Kind == GitRepositoryBindingKind.ApprovedExplicitRoot ? binding.ApprovedRoot : session?.Context.GitRoot;
		if(string.IsNullOrWhiteSpace(root))
		{
			return null;
		}
		List<GitChangedFileModel> changed = await gitFeature.GetChangedFiles(root);
		cancellationToken.ThrowIfCancellationRequested();
		return new GitRepositorySnapshot
		{
			RuntimeSessionKey = runtimeSessionKey,
			IdentityEpoch = identityEpoch,
			Generation = Interlocked.Increment(ref _generation),
			CapturedAt = DateTimeOffset.UtcNow,
			Repository = new(Path.GetFullPath(root), session?.Context.Repository, session?.Context.Branch),
			Files = [.. changed.Select(file => new GitChangedFileSnapshot
			{
				RelativePath = file.Path,
				Status = file.Status switch
				{
					GitFileStatusEnum.Added => GitChangeStatus.Added,
					GitFileStatusEnum.Deleted => GitChangeStatus.Deleted,
					GitFileStatusEnum.Renamed => GitChangeStatus.Renamed,
					GitFileStatusEnum.Untracked => GitChangeStatus.Untracked,
					_ => GitChangeStatus.Modified
				},
				Diff = file.Diff
			})]
		};
	}

	public IDisposable Subscribe(string runtimeSessionKey, long identityEpoch, Action<GitSourceChange> onChanged)
	{
		void Handler(SessionStateChange change)
		{
			if(change.SessionId is null || change.SessionId == runtimeSessionKey)
			{
				onChanged(new(runtimeSessionKey, identityEpoch, Interlocked.Increment(ref _generation)));
			}
		}
		sessions.OnSessionStateChanged += Handler;
		return new CallbackDisposable(() => sessions.OnSessionStateChanged -= Handler);
	}

	public ValueTask<bool> RevealFileAsync(string runtimeSessionKey, long identityEpoch, string canonicalRepositoryRoot, string relativePath, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		string root = Path.GetFullPath(canonicalRepositoryRoot);
		string path = Path.GetFullPath(Path.Combine(root, relativePath));
		if(!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
		{
			return ValueTask.FromResult(false);
		}
		FileUtil.RevealFile(path);
		return ValueTask.FromResult(true);
	}
}

sealed partial class CallbackDisposable(Action callback) : IDisposable
{
	Action? _callback = callback;
	public void Dispose() => Interlocked.Exchange(ref _callback, null)?.Invoke();
}
