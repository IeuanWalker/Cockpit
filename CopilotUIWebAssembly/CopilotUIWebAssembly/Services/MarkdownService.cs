using Markdig;

namespace CopilotUIWebAssembly.Services;

public class MarkdownService
{
	readonly MarkdownPipeline _pipeline;

	public MarkdownService()
	{
		_pipeline = new MarkdownPipelineBuilder()
			.UseAdvancedExtensions()
			.UseSoftlineBreakAsHardlineBreak()
			.DisableHtml()
			.Build();
	}

	public string ToHtml(string markdown)
	{
		if(string.IsNullOrWhiteSpace(markdown))
		{
			return string.Empty;
		}

		return Markdown.ToHtml(markdown, _pipeline);
	}
}
