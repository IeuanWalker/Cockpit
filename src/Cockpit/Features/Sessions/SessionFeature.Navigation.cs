using Cockpit.Features.Sessions.Models;

namespace Cockpit.Features.Sessions;

public sealed partial class SessionFeature
{
	async Task SwitchCurrentSessionAsync(SessionModel session)
	{
		_currentWatcher?.Dispose();
		_currentWatcher = null;

		_sessionListFeature.SetCurrentSession(session);

		if(session.Context.GitRoot is null)
		{
			return;
		}

		string gitRoot = session.Context.GitRoot;
		session.Context.EditedFiles = await _gitFeature.GetChangedFiles(gitRoot);
		_sessionListFeature.NotifyStateChanged();

		_currentWatcher = _gitFeature.Watch(gitRoot, async () =>
		{
			if(_sessionListFeature.CurrentSession?.Id != session.Id)
			{
				return;
			}

			string? branch = await _gitFeature.GetBranch(gitRoot);
			if(branch is not null && branch != session.Context.Branch)
			{
				session.Context.Branch = branch;
			}

			session.Context.EditedFiles = await _gitFeature.GetChangedFiles(gitRoot);
			_sessionListFeature.NotifyStateChanged();
		});
	}
}
