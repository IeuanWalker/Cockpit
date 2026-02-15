namespace Cockpit.Features.Permissions.Models;

public class PermissionRequestModel
{
	public string Id { get; init; } = Guid.NewGuid().ToString();
	public DateTime Requested { get; init; } = DateTime.UtcNow;
	public required string SessionId { get; init; }
	public required string Command { get; init; }
	public required string RequestTitle { get; init; }
	public required string Intention { get; init; }
	public required bool CanApproveGlobally { get; init; }
	public required bool CanApproveForSession { get; init; }
	public required string FullRequestJson { get; init; }

	/// <summary>
	/// TaskCompletionSource to await user decision
	/// </summary>
	public TaskCompletionSource<PermissionDecisionModel> CompletionSource { get; } = new();

	/// <summary>
	/// Get the awaitable task for user decision
	/// </summary>
	public Task<PermissionDecisionModel> GetDecisionAsync() => CompletionSource.Task;
}
