namespace Cockpit.Features.Sessions;

public partial class SessionFeature
{
	public void SetCurrentSessionContextDirectory(string directory)
	{
		if(_sessionListFeature.CurrentSession is null || string.IsNullOrWhiteSpace(directory))
		{
			return;
		}

		_sessionListFeature.CurrentSession.Context.CurrentDirectory = directory;
		_sessionListFeature.NotifyStateChanged();
	}

	public void ToggleCurrentSessionContextSkill(string skill)
	{
		if(_sessionListFeature.CurrentSession is null || string.IsNullOrWhiteSpace(skill))
		{
			return;
		}

		if(!_sessionListFeature.CurrentSession.Context.AgentSkills.Remove(skill))
		{
			_sessionListFeature.CurrentSession.Context.AgentSkills.Add(skill);
		}
		_sessionListFeature.NotifyStateChanged();
	}

	public void ToggleCurrentSessionContextCommand(string command)
	{
		if(_sessionListFeature.CurrentSession is null || string.IsNullOrWhiteSpace(command))
		{
			return;
		}

		if(!_sessionListFeature.CurrentSession.Context.AllowedCommands.Remove(command))
		{
			_sessionListFeature.CurrentSession.Context.AllowedCommands.Add(command);
		}
		_sessionListFeature.NotifyStateChanged();
	}
}
