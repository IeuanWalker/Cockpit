using System.Text;
using System.Text.RegularExpressions;
using Markdig;

namespace Cockpit.Features.Markdown;

public partial class MarkdownFeature
{
	readonly MarkdownPipeline _pipeline;

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

	const string copyButton = "<button type=\"button\" class=\"code-copy-button\">Copy</button>";

	static string WrapCodeBlocks(string html)
	{
		if(!html.Contains("<pre"))
		{
			return html;
		}

		html = preOpenTagRegex().Replace(html, m => $"<div class=\"code-block\">{copyButton}{m.Value}");
		html = html.Replace("</pre>", "</pre></div>");
		return html;
	}

	public string ToHtml(string markdown)
	{
		if(string.IsNullOrWhiteSpace(markdown))
		{
			return string.Empty;
		}

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

	[GeneratedRegex(@"<pre(?:\s[^>]*)?>", RegexOptions.Compiled)]
	private static partial Regex preOpenTagRegex();
}
