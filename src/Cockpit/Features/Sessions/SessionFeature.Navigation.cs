using Cockpit.Features.Git.Models;
using Cockpit.Features.Sessions.Models;

namespace Cockpit.Features.Sessions;

public sealed partial class SessionFeature
{
	async Task SwitchCurrentSessionAsync(SessionModel session)
	{
		_currentWatcher?.Dispose();
		_currentWatcher = null;

		_sessionListFeature.SetCurrentSession(session);

		SessionWorkingDirectoryNormalizer.ApplyContextConsistency(session.Context);
		if(session.Context.CurrentWorkingDirectory is null)
		{
			_sessionListFeature.NotifyStateChanged();
			return;
		}

		if(session.Context.GitRoot is null)
		{
			session.Context.EditedFiles = [];
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
			List<GitChangedFileModel> editedFiles = await _gitFeature.GetChangedFiles(gitRoot);

			MainThread.BeginInvokeOnMainThread(() =>
			{
				if(_sessionListFeature.CurrentSession?.Id != session.Id)
				{
					return;
				}

				if(branch is not null && branch != session.Context.Branch)
				{
					session.Context.Branch = branch;
				}
				session.Context.EditedFiles = editedFiles;
				_sessionListFeature.NotifyStateChanged();
			});
		});
	}
}
