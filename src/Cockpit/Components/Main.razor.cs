using Cockpit.Features.Sessions;
using Microsoft.AspNetCore.Components;

namespace Cockpit.Components;

public partial class Main : ComponentBase
{
	readonly SessionListFeature _sessionListFeature;

	public Main(SessionListFeature sessionListFeature)
	{
		_sessionListFeature = sessionListFeature;
	}
}
