using Cockpit.Features.Git.Models;
using Cockpit.Features.Sessions.Models;

namespace Cockpit.Features.Sessions;

public sealed partial class SessionFeature
{
	async Task<bool> SwitchCurrentSessionAsync(
		SessionModel session,
		bool? rightSidebarOpenOnCommit = null,
		long? navigationRequestId = null,
		string? requiredCurrentSessionId = null)
	{
		SessionWorkingDirectoryNormalizer.ApplyContextConsistency(session.Context);
		List<GitChangedFileModel>? editedFiles = null;
		string? gitRoot = session.Context.GitRoot;
		if(session.Context.CurrentWorkingDirectory is not null && gitRoot is not null)
		{
			// Complete fallible preparation before publishing this session or applying its
			// cold-open sidebar preference. A failed/stale load must not disturb the view.
			editedFiles = await _gitFeature.GetChangedFiles(gitRoot);
		}

		// Git preparation above is asynchronous. A newer navigation can win while it is in
		// flight, so re-check ownership at the actual commit point.
		if(!CanCommitNavigation(navigationRequestId, requiredCurrentSessionId))
		{
			return false;
		}

		_currentWatcher?.Dispose();
		_currentWatcher = null;
		if(editedFiles is not null)
		{
			session.Context.EditedFiles = editedFiles;
		}
		else if(gitRoot is null)
		{
			session.Context.EditedFiles = [];
		}

		if(rightSidebarOpenOnCommit.HasValue)
		{
			_uiStateFeature.SetRightSidebarOpen(rightSidebarOpenOnCommit.Value);
		}
		_sessionListFeature.SetCurrentSession(session);
		_sessionListFeature.NotifyStateChanged();

		if(gitRoot is null)
		{
			return true;
		}

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
		return true;
	}
}
