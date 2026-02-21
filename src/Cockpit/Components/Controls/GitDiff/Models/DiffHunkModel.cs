namespace Cockpit.Components.Controls.GitDiff.Models;

public sealed class DiffHunkModel
{
	public required string Header { get; init; }
	public required List<DiffLineModel> Lines { get; init; }
}
