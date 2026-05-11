using System.Text;
using System.Text.RegularExpressions;
using Markdig;

namespace Cockpit.Features.Markdown;

/// <summary>
/// Converts raw markdown to sanitised HTML using Markdig.
/// Handles zero-width spaces, lone surrogate sanitisation, and code-block wrapping.
/// Results are cached per instance (up to <see cref="MaxCacheEntries"/> entries) to avoid
/// repeated parsing of the same content — useful when components re-render without new messages.
/// </summary>
public sealed partial class MarkdownFeature : IMarkdownFeature
{
	readonly MarkdownPipeline _pipeline;
	// Dictionary is intentionally non-thread-safe: MarkdownFeature is registered as Scoped
	// (per Blazor circuit), and each circuit runs on a single synchronization context.
	readonly Dictionary<string, string> _cache = new(StringComparer.Ordinal);
	const int MaxCacheEntries = 64;

	public MarkdownFeature()
	{
		_pipeline = new MarkdownPipelineBuilder()
			.DisableHtml()
			.UseAdvancedExtensions()
			.UseSoftlineBreakAsHardlineBreak()
			.UseEmojiAndSmiley()
			.UseGridTables()
			.UseListExtras()
			.UsePipeTables()
			.UseTaskLists()
			.Build();
	}

	/// <summary>Replaces lone UTF-16 surrogate characters with U+FFFD (replacement character).</summary>
	static string ReplaceLoneSurrogates(string input)
	{
		StringBuilder sb = new(input.Length);
		for(int i = 0; i < input.Length; i++)
		{
			char c = input[i];
			if(char.IsHighSurrogate(c))
			{
				if(i + 1 < input.Length && char.IsLowSurrogate(input[i + 1]))
				{
					sb.Append(c);
					sb.Append(input[++i]);
				}
				else
				{
					sb.Append('\uFFFD');
				}
			}
			else if(char.IsLowSurrogate(c))
			{
				sb.Append('\uFFFD');
			}
			else
			{
				sb.Append(c);
			}
		}
		return sb.ToString();
	}

	const string CopyButton = "<button type=\"button\" class=\"code-copy-button\">Copy</button>";

	static string WrapCodeBlocks(string html)
	{
		if(!html.Contains("<pre"))
		{
			return html;
		}

		html = PreOpenTagRegex().Replace(html, m => $"<div class=\"code-block\">{CopyButton}{m.Value}");
		html = html.Replace("</pre>", "</pre></div>");
		return html;
	}

	string ComputeHtml(string markdown)
	{
		// Strip zero-width spaces that the chat input inserts as cursor anchors.
		// If present on a code-fence line they prevent Markdig recognising the fence
		// (U+200B is not whitespace in .NET) — causing blocks to swallow extra content.
		if(markdown.Contains('\u200B'))
		{
			markdown = markdown.Replace("\u200B", string.Empty);
		}

		// Replace lone surrogates and other invalid Unicode that Markdig rejects
		markdown = ReplaceLoneSurrogates(markdown);

		return WrapCodeBlocks(Markdig.Markdown.ToHtml(markdown, _pipeline));
	}

	/// <inheritdoc />
	public string ToHtml(string? markdown)
	{
		if(string.IsNullOrWhiteSpace(markdown))
		{
			return string.Empty;
		}

		if(_cache.TryGetValue(markdown, out string? cached))
		{
			return cached;
		}

		string result = ComputeHtml(markdown);

		if(_cache.Count >= MaxCacheEntries)
		{
			_cache.Clear();
		}

		_cache[markdown] = result;
		return result;
	}

	[GeneratedRegex(@"<pre(?:\s[^>]*)?>", RegexOptions.Compiled)]
	private static partial Regex PreOpenTagRegex();
}
