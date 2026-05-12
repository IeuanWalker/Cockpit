using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging;

namespace Cockpit.Features.Hooks;

public sealed class SessionHooksFactory
{
	const int sessionSummaryPreviewLength = 120;

	readonly ILogger<SessionHooksFactory> _logger;

	public SessionHooksFactory(ILogger<SessionHooksFactory> logger)
	{
		_logger = logger;
	}

	public SessionHooks CreateHooks(string? modelId = null, string? reasoningEffort = null, string? configuredWorkingDirectory = null, bool disableResume = false)
	{
		return new SessionHooks
		{
			OnSessionStart = (input, _) =>
			{
				_logger.LogInformation(
					"SDK hook session.start - source: {Source}, model: {Model}, reasoningEffort: {ReasoningEffort}, disableResume: {DisableResume}, configuredWorkingDirectory: {ConfiguredWorkingDirectory}, sessionWorkingDirectory: {SessionWorkingDirectory}, initialPromptLength: {InitialPromptLength}",
					input.Source,
					modelId ?? "unknown",
					reasoningEffort ?? "default",
					disableResume,
					configuredWorkingDirectory,
					input.Cwd,
					GetTextLength(input.InitialPrompt));

				return Task.FromResult<SessionStartHookOutput?>(null);
			},
			OnSessionEnd = (input, _) =>
			{
				_logger.LogInformation(
					"SDK hook session.end - reason: {Reason}, model: {Model}, reasoningEffort: {ReasoningEffort}, configuredWorkingDirectory: {ConfiguredWorkingDirectory}, sessionWorkingDirectory: {SessionWorkingDirectory}, finalMessageLength: {FinalMessageLength}, finalMessageSummary: {FinalMessageSummary}, hasError: {HasError}, error: {Error}",
					input.Reason,
					modelId ?? "unknown",
					reasoningEffort ?? "default",
					configuredWorkingDirectory,
					input.Cwd,
					GetTextLength(input.FinalMessage),
					CreateSummaryPreview(input.FinalMessage),
					!string.IsNullOrWhiteSpace(input.Error),
					input.Error);

				return Task.FromResult<SessionEndHookOutput?>(null);
			},
			OnErrorOccurred = (input, _) =>
			{
				string errorHandling = input.Recoverable
					? "retry"
					: ShouldSkipError(input.ErrorContext)
						? "skip"
						: "abort";
				ErrorOccurredHookOutput output = new()
				{
					ErrorHandling = errorHandling
				};

				if(string.Equals(errorHandling, "retry", StringComparison.Ordinal))
				{
					output.RetryCount = 1;
				}

				_logger.LogError(
					"SDK hook error.occurred - context: {ErrorContext}, recoverable: {Recoverable}, handling: {ErrorHandling}, model: {Model}, reasoningEffort: {ReasoningEffort}, configuredWorkingDirectory: {ConfiguredWorkingDirectory}, sessionWorkingDirectory: {SessionWorkingDirectory}, error: {Error}",
					input.ErrorContext,
					input.Recoverable,
					output.ErrorHandling,
					modelId ?? "unknown",
					reasoningEffort ?? "default",
					configuredWorkingDirectory,
					input.Cwd,
					input.Error);

				return Task.FromResult<ErrorOccurredHookOutput?>(output);
			}
		};
	}

	static int GetTextLength(string? value)
	{
		return string.IsNullOrEmpty(value) ? 0 : value.Length;
	}

	static string? CreateSummaryPreview(string? value)
	{
		if(string.IsNullOrWhiteSpace(value))
		{
			return null;
		}

		string trimmedValue = value.Trim();
		return trimmedValue.Length <= sessionSummaryPreviewLength
			? trimmedValue
			: trimmedValue[..(sessionSummaryPreviewLength - 3)] + "...";
	}

	static bool ShouldSkipError(string? errorContext)
	{
		return string.Equals(errorContext, "tool_execution", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(errorContext, "user_input", StringComparison.OrdinalIgnoreCase);
	}
}
