using System.Text.Json.Serialization;

namespace Cockpit.Tools.FileExplorer;

public sealed record FileExplorerState
{
	public const int CurrentSchemaVersion = 1;
	public string? RootPath { get; init; }
	public string? CurrentPath { get; init; }
	public string? SelectedPath { get; init; }
	public string? PreviewText { get; init; }
	public string Filter { get; init; } = string.Empty;
}

[JsonSerializable(typeof(FileExplorerState))]
public partial class FileExplorerJsonContext : JsonSerializerContext;

public sealed record FileExplorerEntry(string Name, string FullPath, bool IsDirectory);
