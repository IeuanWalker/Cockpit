using System.Text.RegularExpressions;
using Blazor.Sonner.Services;
using Cockpit.Features.AppSettings;
using Microsoft.Extensions.Logging;

namespace Cockpit.Features.TextToSpeech;

public partial class TextToSpeechFeature : IDisposable
{
	public const float DefaultVoiceVolume = 1.0f;
	public const float DefaultVoicePitch = 1.0f;
	public const float DefaultVoiceRate = 0.2f;

	public event Action? OnStateChanged;

	public string? ActiveMessageId { get; private set; }
	public bool IsSpeaking => ActiveMessageId is not null;

	readonly ITextToSpeech _textToSpeech;
	readonly ILogger<TextToSpeechFeature> _logger;
	readonly ToastService _toastService;
	readonly IAppSettingsFeature _appSettingsFeature;
	CancellationTokenSource? _cts;
	readonly SemaphoreSlim _lock = new(1, 1);

	public TextToSpeechFeature(
		ITextToSpeech textToSpeech,
		ILogger<TextToSpeechFeature> logger,
		ToastService toastService,
		IAppSettingsFeature appSettingsFeature)
	{
		_textToSpeech = textToSpeech;
		_logger = logger;
		_toastService = toastService;
		_appSettingsFeature = appSettingsFeature;
	}

	public async Task<IEnumerable<Locale>> GetLocales()
	{
		return await _textToSpeech.GetLocalesAsync();
	}

	public async Task Speak(string messageId, string text)
	{
		CancellationToken token = default;

		await _lock.WaitAsync();
		try
		{
			// If same message is speaking, stop it
			if(ActiveMessageId == messageId)
			{
				await StopCore();
				return;
			}

			// Stop any current speech first
			await StopCore();

			ActiveMessageId = messageId;
			OnStateChanged?.Invoke();

			_cts = new CancellationTokenSource();
			token = _cts.Token;  // Capture while holding lock to avoid race with Stop()
		}
		finally
		{
			_lock.Release();
		}

		string plainText = StripMarkdown(text);

		try
		{
			SpeechOptions options = await BuildSpeechOptionsAsync();
			await _textToSpeech.SpeakAsync(plainText, options, cancelToken: token);
		}
		catch(OperationCanceledException)
		{
			// Expected when stopped
		}
		catch(Exception ex)
		{
			_logger.LogError(ex, "Text-to-speech error");
			_toastService?.Error("Text-to-Speech Error", opts => opts.Description = ex.Message);
		}
		finally
		{
			await _lock.WaitAsync();
			try
			{
				if(ActiveMessageId == messageId)
				{
					ActiveMessageId = null;
					OnStateChanged?.Invoke();
				}
			}
			finally
			{
				_lock.Release();
			}
		}
	}

	public async Task Stop()
	{
		await _lock.WaitAsync();
		try
		{
			await StopCore();
		}
		finally
		{
			_lock.Release();
		}
	}

	async Task StopCore()
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

	protected virtual async Task<SpeechOptions> BuildSpeechOptionsAsync()
	{
		SpeechOptions options = new()
		{
			Volume = _appSettingsFeature.VoiceVolume,
			Pitch = _appSettingsFeature.VoicePitch,
			Rate = _appSettingsFeature.VoiceRate,
		};

		string localeId = _appSettingsFeature.VoiceLocale;
		if(!string.IsNullOrEmpty(localeId))
		{
			IEnumerable<Locale> locales = await _textToSpeech.GetLocalesAsync();
			options.Locale = locales.FirstOrDefault(l => l.Id == localeId);
		}

		return options;
	}

	internal static string StripMarkdown(string markdown)
	{
		if(string.IsNullOrWhiteSpace(markdown))
		{
			return string.Empty;
		}

		string text = CodeBlockRegex().Replace(markdown, " code block ");
		text = InlineCodeRegex().Replace(text, "$1");
		text = HeadingRegex().Replace(text, string.Empty);
		text = BoldItalicRegex().Replace(text, "$1$2$3$4$5$6");
		text = ImageRegex().Replace(text, string.Empty);
		text = LinkRegex().Replace(text, "$1");
		text = HorizontalRuleRegex().Replace(text, string.Empty);
		text = BlockquoteRegex().Replace(text, string.Empty);
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

	[GeneratedRegex(@"\n{3,}")]
	private static partial Regex ExcessNewlinesRegex();

	public void Dispose()
	{
		Dispose(true);
		GC.SuppressFinalize(this);
	}

	protected virtual void Dispose(bool disposing)
	{
		if(disposing)
		{
			_cts?.Cancel();
			_cts?.Dispose();
			_cts = null;
			_lock.Dispose();
		}
	}
}
