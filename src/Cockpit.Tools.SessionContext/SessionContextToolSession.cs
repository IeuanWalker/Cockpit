using Cockpit.Tooling;
using Cockpit.Tools.SessionContext.Sources;

namespace Cockpit.Tools.SessionContext;

public sealed class SessionContextToolSession : IWorkspaceToolSession
{
	readonly ISessionContextToolSource _source;
	readonly HashSet<SessionContextToolInstance> _instances = [];
	bool _disposed;

	public SessionContextToolSession(WorkspaceSessionInfo session, ISessionContextToolSource source)
	{
		Session = session;
		_source = source;
		IsAvailable = true;
	}

	public WorkspaceSessionInfo Session { get; private set; }
	public bool IsAvailable { get; private set; }
	public string? UnavailabilityReason { get; private set; }
	public event Action? AvailabilityChanged;

	public async ValueTask<IWorkspaceToolInstance> CreateInstanceAsync(
		WorkspaceToolInstanceContext context,
		CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		cancellationToken.ThrowIfCancellationRequested();
		SessionContextToolInstance instance = new(context, _source);
		_instances.Add(instance);
		await instance.RestoreAsync(null, cancellationToken);
		return instance;
	}

	public ValueTask RefreshAvailabilityAsync(WorkspaceSessionInfo session, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		Session = session;
		UpdateAvailability(true);
		return ValueTask.CompletedTask;
	}

	public void UpdateAvailability(bool available, string? reason = null)
	{
		if(IsAvailable == available && UnavailabilityReason == reason)
		{
			return;
		}

		IsAvailable = available;
		UnavailabilityReason = available ? null : reason;
		AvailabilityChanged?.Invoke();
	}

	public async ValueTask DisposeAsync()
	{
		if(_disposed)
		{
			return;
		}

		_disposed = true;
		foreach(SessionContextToolInstance instance in _instances)
		{
			await instance.DisposeAsync();
		}
		_instances.Clear();
	}
}
