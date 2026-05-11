namespace Cockpit.Features.Markdown;

/// <summary>Converts raw markdown text into sanitised HTML.</summary>
public interface IMarkdownFeature
{
	/// <summary>
	/// Converts <paramref name="markdown"/> to HTML using the configured Markdig pipeline.
	/// Returns <see cref="string.Empty"/> for null or whitespace-only input.
	/// </summary>
	string ToHtml(string? markdown);
}
