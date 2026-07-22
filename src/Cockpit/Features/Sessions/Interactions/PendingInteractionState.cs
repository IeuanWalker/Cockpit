using System.Collections.Concurrent;
using Cockpit.Features.ElicitationRequests;
using Cockpit.Features.Permissions.Models;
using Cockpit.Features.Sessions.Models;
using Cockpit.Features.UserInputRequests;

namespace Cockpit.Features.Sessions.Interactions;

/// <summary>
/// Session-scoped state for requests that are waiting on a user interaction.
/// Mutations are coordinated by <see cref="SessionInteractionCoordinator"/>.
/// </summary>
public sealed class PendingInteractionState
{
	readonly ConcurrentDictionary<string, PermissionRequestModel> _permissions = new();
	readonly ConcurrentDictionary<string, UserInputRequestModel> _userInputs = new();
	readonly ConcurrentDictionary<string, ElicitationRequestModel> _elicitations = new();

	public IReadOnlyDictionary<string, PermissionRequestModel> Permissions => _permissions;
	public IReadOnlyDictionary<string, UserInputRequestModel> UserInputs => _userInputs;
	public IReadOnlyDictionary<string, ElicitationRequestModel> Elicitations => _elicitations;

	internal SessionStatusEnum? DisplayStatus { get; set; }
	internal Lock SyncRoot { get; } = new();

	internal bool TryAddPermission(PermissionRequestModel request) => _permissions.TryAdd(request.Id, request);
	internal bool TryAddUserInput(UserInputRequestModel request) => _userInputs.TryAdd(request.Id, request);
	internal bool TryAddElicitation(ElicitationRequestModel request) => _elicitations.TryAdd(request.Id, request);

	internal bool TryRemovePermission(string requestId) => _permissions.TryRemove(requestId, out _);
	internal bool TryRemoveUserInput(string requestId) => _userInputs.TryRemove(requestId, out _);
	internal bool TryRemoveElicitation(string requestId) => _elicitations.TryRemove(requestId, out _);

	internal void ClearPermissions() => _permissions.Clear();
	internal void ClearUserInputs() => _userInputs.Clear();
	internal void ClearElicitations() => _elicitations.Clear();

	internal bool HasPermissions => !_permissions.IsEmpty;
	internal bool HasUserInputs => !_userInputs.IsEmpty;
	internal bool HasElicitations => !_elicitations.IsEmpty;
}
