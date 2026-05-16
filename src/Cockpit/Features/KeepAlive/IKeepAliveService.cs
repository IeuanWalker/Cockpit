namespace Cockpit.Features.KeepAlive;

/// <summary>Prevents the system from sleeping while active.</summary>
public interface IKeepAliveService
{
	void Activate();
	void Deactivate();
}
