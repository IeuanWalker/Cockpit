using Cockpit.Tooling;

namespace Cockpit.Tools.FileExplorer;

public sealed class FileExplorerToolInstance(WorkspaceToolInstanceContext context)
	: WorkspaceToolInstance<FileExplorerState>(context.InstanceId, FileExplorerJsonContext.Default.FileExplorerState)
{
	public event Action? Changed;
	public FileExplorerState ViewState => State;
	protected override int SchemaVersion => FileExplorerState.CurrentSchemaVersion;
	protected override FileExplorerState CreateDefaultState() => new() { RootPath = context.Session.WorkingDirectory, CurrentPath = context.Session.WorkingDirectory };
	public IReadOnlyList<FileExplorerEntry> Entries
	{
		get
		{
			string? path = State.CurrentPath;
			string? root = State.RootPath;
			if(path is null || root is null || !Directory.Exists(path) || !IsWithinRoot(root, path))
			{
				return [];
			}

			try
			{
				return Directory.EnumerateFileSystemEntries(path)
					.Select(value => new FileExplorerEntry(Path.GetFileName(value), value, Directory.Exists(value)))
					.Where(entry => string.IsNullOrWhiteSpace(State.Filter) || entry.Name.Contains(State.Filter, StringComparison.OrdinalIgnoreCase))
					.OrderByDescending(entry => entry.IsDirectory).ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase).ToArray();
			}
			catch { return []; }
		}
	}
	public void SetFilter(string value) { SetState(State with { Filter = value }); Changed?.Invoke(); }
	public async Task OpenAsync(FileExplorerEntry entry)
	{
		if(entry.IsDirectory)
		{
			SetState(State with { CurrentPath = entry.FullPath, SelectedPath = null, PreviewText = null });
		}
		else
		{
			string? preview;
			try
			{
				FileInfo info = new(entry.FullPath);
				preview = info.Length <= 2_000_000 ? await File.ReadAllTextAsync(entry.FullPath) : "File is too large to preview.";
			}
			catch(Exception exception) { preview = exception.Message; }
			SetState(State with { SelectedPath = entry.FullPath, PreviewText = preview });
		}
		Changed?.Invoke();
	}
	public void Up()
	{
		string? root = State.RootPath;
		string? current = State.CurrentPath;
		if(root is null || current is null || !IsWithinRoot(root, current) || string.Equals(Path.GetFullPath(root), Path.GetFullPath(current), StringComparison.OrdinalIgnoreCase))
		{
			return;
		}

		DirectoryInfo? parent = Directory.GetParent(current);
		if(parent is not null && IsWithinRoot(root, parent.FullName))
		{
			SetState(State with { CurrentPath = parent.FullName, SelectedPath = null, PreviewText = null }); Changed?.Invoke();
		}
	}

	static bool IsWithinRoot(string root, string candidate)
	{
		string canonicalRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
		string canonicalCandidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
		return string.Equals(canonicalRoot, canonicalCandidate, StringComparison.OrdinalIgnoreCase) ||
			canonicalCandidate.StartsWith(canonicalRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
	}
}
