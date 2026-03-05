namespace Cockpit.Features.Permissions.Models;

public class PermissionRequestModel
{
	public string Id { get; init; } = Guid.NewGuid().ToString();
	public DateTime Requested { get; init; } = DateTime.UtcNow;
	public required string SessionId { get; init; }
	public required string FullCommand { get; init; }
	public required List<string> Commands { get; init; }
	public required string RequestTitle { get; set; }
	public required string Intention { get; init; }
	public required bool CanApproveGlobally { get; init; }
	public required bool CanApproveForSession { get; init; }
	public required string FullRequestJson { get; init; }
	public bool IsDestructive { get; init; }
	public List<string> FilesToDelete { get; init; } = [];

	/// <summary>
	/// TaskCompletionSource to await user decision
	/// </summary>
	public TaskCompletionSource<PermissionDecisionEnum> CompletionSource { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

	/// <summary>
	/// Get the awaitable task for user decision
	/// </summary>
	public Task<PermissionDecisionEnum> GetDecisionAsync() => CompletionSource.Task;
}
