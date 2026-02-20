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

	public string ToHtml(string markdown)
	{
		if(string.IsNullOrWhiteSpace(markdown))
		{
			return string.Empty;
		}

		return Markdig.Markdown.ToHtml(markdown, _pipeline);
	}
}
