namespace Cockpit.Tools.SessionContext.Sources;

/// <summary>
/// Executable-host adapter used by the tool. Implementations must return detached,
/// immutable snapshots and filter notifications to the requested runtime session.
/// </summary>
public interface ISessionContextToolSource
{
	ValueTask<SessionContextSnapshot?> GetSnapshotAsync(
		string runtimeSessionKey,
		long identityEpoch,
		CancellationToken cancellationToken);

	IDisposable Subscribe(
		string runtimeSessionKey,
		long identityEpoch,
		Action<SessionContextSourceChange> onChanged);

	ValueTask RefreshSectionAsync(
		string runtimeSessionKey,
		long identityEpoch,
		string sectionId,
		CancellationToken cancellationToken);

	ValueTask<bool> AddItemToMessageAsync(
		string runtimeSessionKey,
		long identityEpoch,
		string sectionId,
		string itemKey,
		CancellationToken cancellationToken);

	ValueTask<SessionContextCommandResult> ExecuteAsync(
		string runtimeSessionKey,
		long identityEpoch,
		SessionContextCommandRequest request,
		CancellationToken cancellationToken);
}
