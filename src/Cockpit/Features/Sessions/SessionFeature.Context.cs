namespace Cockpit.Features.Sessions;

public sealed partial class SessionFeature
{
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
}
