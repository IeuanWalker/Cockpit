using System.Text;
using Markdig;

namespace Cockpit.Features.Markdown;

public class MarkdownFeature
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

	public string ToHtml(string markdown)
	{
		if(string.IsNullOrWhiteSpace(markdown))
		{
			return string.Empty;
		}

		// Replace lone surrogates and other invalid Unicode that Markdig rejects
		markdown = ReplaceLoneSurrogates(markdown);

		return Markdig.Markdown.ToHtml(markdown, _pipeline);
	}
}
