using GitHub.Copilot;

namespace Cockpit.Features.Sessions.Models;

public enum SessionAgentModeEnum
{
	Interactive,
	Plan,
	Autopilot
}

public static class SessionAgentModeExtensions
{
	public static SessionMode ToSdkSessionMode(this SessionAgentModeEnum mode) => mode switch
	{
		SessionAgentModeEnum.Plan => SessionMode.Plan,
		SessionAgentModeEnum.Autopilot => SessionMode.Autopilot,
		_ => SessionMode.Interactive
	};

	public static string ToDisplayLabel(this SessionAgentModeEnum mode) => mode switch
	{
		SessionAgentModeEnum.Plan => "Plan",
		SessionAgentModeEnum.Autopilot => "Autopilot",
		_ => "Interactive"
	};
}
