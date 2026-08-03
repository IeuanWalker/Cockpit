using Cockpit.Features.Sessions;
using Shouldly;

namespace Cockpit.UnitTests.Features.Sessions;

public sealed class SessionLoadNotificationPolicyTests
{
	[Fact]
	public void SuccessfulHistoryReplacement_ForAlreadySelectedSession_RequestsConversationReset()
	{
		SessionChangeKind kind = SessionLoadNotificationPolicy.GetSuccessfulHistoryReplacementKind(selectedSessionId: "selected", loadedSessionId: "selected");

		kind.ShouldBe(SessionChangeKind.ConversationReset | SessionChangeKind.ConversationStructure);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("background")]
	public void SuccessfulHistoryReplacement_ForSessionThatWasNotSelected_DoesNotDuplicateSwitchNotification(string? selectedSessionId)
	{
		SessionChangeKind kind = SessionLoadNotificationPolicy.GetSuccessfulHistoryReplacementKind(selectedSessionId, loadedSessionId: "loaded");

		kind.ShouldBe(SessionChangeKind.None);
	}

	[Fact]
	public void SuccessfulHistoryReplacement_UsesOrdinalSessionIdentity()
	{
		SessionChangeKind kind = SessionLoadNotificationPolicy.GetSuccessfulHistoryReplacementKind(selectedSessionId: "SESSION", loadedSessionId: "session");

		kind.ShouldBe(SessionChangeKind.None);
	}
}
