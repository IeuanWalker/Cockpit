using Cockpit.Tooling;
using Cockpit.Tools.GitChanges.Sources;

namespace Cockpit.Tools.GitChanges;

public sealed class GitChangesToolSession : IWorkspaceToolSession
{
	readonly IGitChangesToolSource _source;
	readonly HashSet<GitChangesToolInstance> _instances = [];
	bool _disposed;

	public GitChangesToolSession(WorkspaceSessionInfo session, IGitChangesToolSource source, GitAvailability availability)
	{
		Session = session;
		_source = source;
		Apply(availability);
	}

	public WorkspaceSessionInfo Session { get; private set; }
	public bool IsAvailable { get; private set; }
	public string? UnavailabilityReason { get; private set; }
	public event Action? AvailabilityChanged;

	public async ValueTask RefreshAvailabilityAsync(WorkspaceSessionInfo session, CancellationToken cancellationToken)
	{
		Session = session;
		GitAvailability availability = await _source.GetAvailabilityAsync(session.RuntimeSessionKey.Value,
			session.IdentityEpoch, session.RepositoryRoot, cancellationToken);
		bool changed = availability.IsAvailable != IsAvailable || availability.Reason != UnavailabilityReason;
		Apply(availability);
		if(changed)
		{
			AvailabilityChanged?.Invoke();
		}
	}

	public async ValueTask<IWorkspaceToolInstance> CreateInstanceAsync(WorkspaceToolInstanceContext context, CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		GitChangesToolInstance instance = new(context, _source);
		_instances.Add(instance);
		await instance.RestoreAsync(null, cancellationToken);
		return instance;
	}

	void Apply(GitAvailability availability)
	{
		IsAvailable = availability.IsAvailable;
		UnavailabilityReason = availability.IsAvailable ? null : availability.Reason;
	}

	public async ValueTask DisposeAsync()
	{
		if(_disposed)
		{
			return;
		}

		_disposed = true;
		foreach(GitChangesToolInstance instance in _instances)
		{
			await instance.DisposeAsync();
		}

		_instances.Clear();
	}
}
