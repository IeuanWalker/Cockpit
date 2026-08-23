using Cockpit.Extensions;
using Microsoft.Extensions.Logging;

namespace Cockpit.Features.Sessions;

/// <summary>
/// Persists the session and project identifiers pinned in the session list.
/// </summary>
public sealed class PinnedItemsFeature
{
	const int currentVersion = 1;

	static readonly StringComparer projectIdComparer = OperatingSystem.IsWindows()
		? StringComparer.OrdinalIgnoreCase
		: StringComparer.Ordinal;

	readonly ILogger<PinnedItemsFeature> _logger;
	readonly string _filePath;
	readonly SemaphoreSlim _lock = new(1, 1);
	readonly Task _initializationTask;

	HashSet<string> _sessionIds = new(StringComparer.Ordinal);
	HashSet<string> _projectIds = new(projectIdComparer);

	public PinnedItemsFeature(ILogger<PinnedItemsFeature> logger, string? filePath = null)
	{
		_logger = logger;
		_filePath = filePath ?? Path.Combine(FileSystem.AppDataDirectory, "session-pins.json");
		_initializationTask = LoadAsync();
	}

	public event Action? OnChanged;

	/// <summary>
	/// Waits until the persisted pins have been loaded. Mutating operations do this automatically.
	/// </summary>
	public Task InitializeAsync() => _initializationTask;

	public bool IsSessionPinned(string sessionId) =>
		!string.IsNullOrWhiteSpace(sessionId) && Volatile.Read(ref _sessionIds).Contains(sessionId);

	public bool IsProjectPinned(string projectId) =>
		!string.IsNullOrWhiteSpace(projectId) && Volatile.Read(ref _projectIds).Contains(projectId);

	public async Task ToggleSessionAsync(string sessionId)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
		await _initializationTask.ConfigureAwait(false);

		await _lock.WaitAsync().ConfigureAwait(false);
		try
		{
			HashSet<string> updated = new(_sessionIds, StringComparer.Ordinal);
			if(!updated.Remove(sessionId))
			{
				updated.Add(sessionId);
			}

			Volatile.Write(ref _sessionIds, updated);
			await SaveAsync().ConfigureAwait(false);
		}
		finally
		{
			_lock.Release();
		}

		NotifyChanged();
	}

	public async Task ToggleProjectAsync(string projectId)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
		await _initializationTask.ConfigureAwait(false);

		await _lock.WaitAsync().ConfigureAwait(false);
		try
		{
			HashSet<string> updated = new(_projectIds, projectIdComparer);
			if(!updated.Remove(projectId))
			{
				updated.Add(projectId);
			}

			Volatile.Write(ref _projectIds, updated);
			await SaveAsync().ConfigureAwait(false);
		}
		finally
		{
			_lock.Release();
		}

		NotifyChanged();
	}

	/// <summary>
	/// Transfers a pin when an existing session is assigned a replacement SDK identifier.
	/// </summary>
	public async Task ReplaceSessionIdAsync(string previousSessionId, string replacementSessionId)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(previousSessionId);
		ArgumentException.ThrowIfNullOrWhiteSpace(replacementSessionId);

		if(string.Equals(previousSessionId, replacementSessionId, StringComparison.Ordinal))
		{
			return;
		}

		await _initializationTask.ConfigureAwait(false);

		bool changed = false;
		await _lock.WaitAsync().ConfigureAwait(false);
		try
		{
			if(_sessionIds.Contains(previousSessionId))
			{
				HashSet<string> updated = new(_sessionIds, StringComparer.Ordinal);
				updated.Remove(previousSessionId);
				updated.Add(replacementSessionId);
				Volatile.Write(ref _sessionIds, updated);
				await SaveAsync().ConfigureAwait(false);
				changed = true;
			}
		}
		finally
		{
			_lock.Release();
		}

		if(changed)
		{
			NotifyChanged();
		}
	}

	/// <summary>
	/// Removes pins whose corresponding session or project no longer exists, migrating project aliases
	/// to the current canonical project identifiers when supplied.
	/// </summary>
	public async Task ReconcileAsync(
		IReadOnlySet<string> validSessionIds,
		IReadOnlySet<string> validProjectIds,
		IReadOnlyDictionary<string, string>? projectIdAliases = null)
	{
		ArgumentNullException.ThrowIfNull(validSessionIds);
		ArgumentNullException.ThrowIfNull(validProjectIds);

		await _initializationTask.ConfigureAwait(false);

		bool changed = false;
		await _lock.WaitAsync().ConfigureAwait(false);
		try
		{
			HashSet<string> validSessions = new(validSessionIds, StringComparer.Ordinal);
			HashSet<string> validProjects = new(validProjectIds, projectIdComparer);
			Dictionary<string, string> projectAliases = projectIdAliases is null
				? new(projectIdComparer)
				: new(projectIdAliases, projectIdComparer);
			HashSet<string> reconciledSessions = new(
				_sessionIds.Where(validSessions.Contains),
				StringComparer.Ordinal);
			HashSet<string> reconciledProjects = new(projectIdComparer);
			foreach(string projectId in _projectIds)
			{
				if(validProjects.Contains(projectId))
				{
					reconciledProjects.Add(projectId);
				}
				else if(projectAliases.TryGetValue(projectId, out string? canonicalProjectId) &&
					validProjects.Contains(canonicalProjectId))
				{
					reconciledProjects.Add(canonicalProjectId);
				}
			}

			changed = !_sessionIds.SetEquals(reconciledSessions) ||
				!_projectIds.SetEquals(reconciledProjects);
			if(changed)
			{
				Volatile.Write(ref _sessionIds, reconciledSessions);
				Volatile.Write(ref _projectIds, reconciledProjects);
				await SaveAsync().ConfigureAwait(false);
			}
		}
		finally
		{
			_lock.Release();
		}

		if(changed)
		{
			NotifyChanged();
		}
	}

	async Task LoadAsync()
	{
		if(!File.Exists(_filePath))
		{
			return;
		}

		try
		{
			string json = await File.ReadAllTextAsync(_filePath).ConfigureAwait(false);
			PinnedItemsFile? file = json.DeserializeJson<PinnedItemsFile>();
			if(file is null)
			{
				return;
			}

			if(file.Version != currentVersion)
			{
				_logger.LogWarning(
					"Cannot load pinned items version {Version} from {Path}; expected version {ExpectedVersion}",
					file.Version,
					_filePath,
					currentVersion);
				return;
			}

			HashSet<string> sessionIds = new(
				(file.SessionIds ?? []).Where(id => !string.IsNullOrWhiteSpace(id)),
				StringComparer.Ordinal);
			HashSet<string> projectIds = new(
				(file.ProjectIds ?? []).Where(id => !string.IsNullOrWhiteSpace(id)),
				projectIdComparer);

			Volatile.Write(ref _sessionIds, sessionIds);
			Volatile.Write(ref _projectIds, projectIds);
		}
		catch(Exception ex)
		{
			_logger.LogWarning(ex, "Failed to load pinned items from {Path}", _filePath);
		}
	}

	async Task SaveAsync()
	{
		string tempPath = _filePath + ".tmp";
		try
		{
			string? directory = Path.GetDirectoryName(_filePath);
			if(!string.IsNullOrWhiteSpace(directory))
			{
				Directory.CreateDirectory(directory);
			}

			PinnedItemsFile file = new()
			{
				Version = currentVersion,
				SessionIds = [.. _sessionIds.Order(StringComparer.Ordinal)],
				ProjectIds = [.. _projectIds.Order(projectIdComparer)]
			};
			string json = file.SerializeJson()!;

			await File.WriteAllTextAsync(tempPath, json).ConfigureAwait(false);
			File.Move(tempPath, _filePath, overwrite: true);
		}
		catch(Exception ex)
		{
			_logger.LogWarning(ex, "Failed to save pinned items to {Path}", _filePath);
		}
		finally
		{
			try
			{
				File.Delete(tempPath);
			}
			catch(Exception ex)
			{
				_logger.LogDebug(ex, "Failed to remove temporary pinned items file {Path}", tempPath);
			}
		}
	}

	void NotifyChanged()
	{
		Action? handlers = OnChanged;
		if(handlers is null)
		{
			return;
		}

		foreach(Action handler in handlers.GetInvocationList().Cast<Action>())
		{
			try
			{
				handler();
			}
			catch(Exception ex)
			{
				_logger.LogDebug(ex, "Pinned items change handler threw");
			}
		}
	}

	sealed class PinnedItemsFile
	{
		public int Version { get; init; }
		public List<string>? SessionIds { get; init; }
		public List<string>? ProjectIds { get; init; }
	}
}
