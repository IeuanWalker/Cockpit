using GitHub.Copilot;

namespace Cockpit.Features.ElicitationRequests;

public class ElicitationRequestModel
{
	public string Id { get; init; } = Guid.NewGuid().ToString();
	public DateTime Requested { get; init; } = DateTime.UtcNow;
	public required string SessionId { get; init; }
	public required string Message { get; init; }
	public ElicitationSchema? Schema { get; init; }
	public required ElicitationSchemaField[] Fields { get; init; }
	public ElicitationRequestedMode? Mode { get; init; }
	public required string ElicitationSource { get; init; }
	public string? Url { get; init; }

	/// <summary>
	/// TaskCompletionSource to await user response. Non-nullable — abandonment uses <c>Action = Cancel</c>.
	/// </summary>
	public TaskCompletionSource<ElicitationResult> CompletionSource { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

	/// <summary>
	/// Gets the awaitable task for the user's response.
	/// </summary>
	public Task<ElicitationResult> GetResponseAsync() => CompletionSource.Task;
}
