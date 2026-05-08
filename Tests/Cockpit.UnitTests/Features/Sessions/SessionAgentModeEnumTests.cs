using Cockpit.Features.Sessions.Models;
using Shouldly;

namespace Cockpit.UnitTests.Features.Sessions;

public class SessionAgentModeEnumTests
{
	[Theory]
	[InlineData(SessionAgentModeEnum.Interactive, "Default")]
	[InlineData(SessionAgentModeEnum.Plan, "Plan")]
	[InlineData(SessionAgentModeEnum.Autopilot, "Autopilot")]
	public void ToDisplayLabel_AllValues_ReturnExpectedLabel(SessionAgentModeEnum mode, string expectedLabel)
	{
		mode.ToDisplayLabel().ShouldBe(expectedLabel);
	}
}