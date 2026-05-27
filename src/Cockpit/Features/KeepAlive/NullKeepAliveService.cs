namespace Cockpit.Features.KeepAlive;

sealed class NullKeepAliveService : IKeepAliveService
{
	public void Activate() { }
	public void Deactivate() { }
}
