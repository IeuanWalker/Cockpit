namespace Cockpit.Features.UserInputs.Models;

public class UserInputRequestModel
{
	public string Id { get; init; } = Guid.NewGuid().ToString();
	public DateTime Requested { get; init; } = DateTime.UtcNow;
	public required string SessionId { get; init; }
	public required string Question { get; init; }
	public string? Placeholder { get; init; }
	public bool AllowsTextInput { get; init; } = true;
	public List<string> Choices { get; init; } = [];
	public bool IsRequired { get; init; } = true;
	public required string FullRequestJson { get; init; }

	/// <summary>
	/// TaskCompletionSource to await user response
	/// </summary>
	public TaskCompletionSource<string?> CompletionSource { get; } = new();

	/// <summary>
	/// Get the awaitable task for user response
	/// </summary>
	public Task<string?> GetResponseAsync() => CompletionSource.Task;
}
