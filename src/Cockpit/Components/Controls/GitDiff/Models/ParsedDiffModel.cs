namespace Cockpit.Components.Controls.GitDiff.Models;

public sealed class ParsedDiffModel
{
	public required string OldPath { get; init; }
	public required string NewPath { get; init; }
	public required List<DiffHunkModel> Hunks { get; init; }
	public required int LinesAdded { get; init; }
	public required int LinesRemoved { get; init; }
}