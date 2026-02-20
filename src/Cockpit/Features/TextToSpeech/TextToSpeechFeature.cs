using System.Text.RegularExpressions;
using MauiTts = Microsoft.Maui.Media.TextToSpeech;

namespace Cockpit.Features.TextToSpeech;

public partial class TextToSpeechFeature
{
	public event Action? OnStateChanged;

	public string? ActiveMessageId { get; private set; }
	public bool IsSpeaking => ActiveMessageId is not null;

	CancellationTokenSource? _cts;

	public async Task Speak(string messageId, string text)
	{
		// If same message is speaking, stop it
		if(ActiveMessageId == messageId)
		{
			await Stop();
			return;
		}

		// Stop any current speech first
		await Stop();

		ActiveMessageId = messageId;
		OnStateChanged?.Invoke();

		_cts = new CancellationTokenSource();
		CancellationToken token = _cts.Token;

		string plainText = StripMarkdown(text);

		try
		{
			await MauiTts.Default.SpeakAsync(plainText, cancelToken: token);
		}
		catch(OperationCanceledException)
		{
			// Expected when stopped
		}
		catch
		{
			// Ignore other errors
		}
		finally
		{
			if(ActiveMessageId == messageId)
			{
				ActiveMessageId = null;
				OnStateChanged?.Invoke();
			}
		}
	}

	public async Task Stop()
	{
		CancellationTokenSource? cts = _cts;
		_cts = null;
		if(cts is not null)
		{
			await cts.CancelAsync();
			cts.Dispose();
		}

		if(ActiveMessageId is not null)
		{
			ActiveMessageId = null;
			OnStateChanged?.Invoke();
		}
	}

	static string StripMarkdown(string markdown)
	{
		if(string.IsNullOrWhiteSpace(markdown))
		{
			return string.Empty;
		}

		string text = CodeBlockRegex().Replace(markdown, " code block ");
		text = InlineCodeRegex().Replace(text, "$1");
		text = HeadingRegex().Replace(text, string.Empty);
		text = BoldItalicRegex().Replace(text, "$1$2$3$4$5$6");
		text = LinkRegex().Replace(text, "$1");
		text = ImageRegex().Replace(text, string.Empty);
		text = HorizontalRuleRegex().Replace(text, string.Empty);
		text = BlockquoteRegex().Replace(text, string.Empty);
		text = UnorderedListRegex().Replace(text, string.Empty);
		text = OrderedListRegex().Replace(text, string.Empty);
		text = ExcessNewlinesRegex().Replace(text, "\n\n");

		return text.Trim();
	}

	[GeneratedRegex(@"```[\s\S]*?```", RegexOptions.Multiline)]
	private static partial Regex CodeBlockRegex();

	[GeneratedRegex(@"`([^`]+)`")]
	private static partial Regex InlineCodeRegex();

	[GeneratedRegex(@"^#{1,6}\s+", RegexOptions.Multiline)]
	private static partial Regex HeadingRegex();

	[GeneratedRegex(@"\*\*\*([^*]+)\*\*\*|\*\*([^*]+)\*\*|\*([^*]+)\*|___([^_]+)___|__([^_]+)__|_([^_]+)_")]
	private static partial Regex BoldItalicRegex();

	[GeneratedRegex(@"\[([^\]]+)\]\([^\)]+\)")]
	private static partial Regex LinkRegex();

	[GeneratedRegex(@"!\[[^\]]*\]\([^\)]+\)")]
	private static partial Regex ImageRegex();

	[GeneratedRegex(@"^[-*_]{3,}$", RegexOptions.Multiline)]
	private static partial Regex HorizontalRuleRegex();

	[GeneratedRegex(@"^>\s+", RegexOptions.Multiline)]
	private static partial Regex BlockquoteRegex();

	[GeneratedRegex(@"^[\-\*\+]\s+", RegexOptions.Multiline)]
	private static partial Regex UnorderedListRegex();

	[GeneratedRegex(@"^\d+\.\s+", RegexOptions.Multiline)]
	private static partial Regex OrderedListRegex();

	[GeneratedRegex(@"\n{3,}")]
	private static partial Regex ExcessNewlinesRegex();
}
