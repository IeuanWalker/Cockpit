using Cockpit.Features.Sessions.Models;
using GitHub.Copilot;
using Shouldly;

namespace Cockpit.UnitTests.Features.Sessions;

public sealed class SessionUiStateTests
{
	static SessionModel CreateSession(string id) => new()
	{
		Id = id,
		Title = "UI state",
		CreatedAt = DateTime.UtcNow,
		LastActivity = DateTime.UtcNow,
		Model = new ModelInfo { Id = "test", Name = "Test Model" },
		Context = new()
		{
			CurrentWorkingDirectory = string.Empty,
			WorkspacePath = null,
			GitRoot = null,
			Repository = null,
			Branch = null
		}
	};

	[Fact]
	public void CompatibilitySurface_ForwardsToUiState()
	{
		SessionModel session = CreateSession("ui-session");
		List<AttachmentModel> attachments =
		[
			new("file.txt", "file.txt", "data:text/plain;base64,dGVzdA==", "text/plain")
		];

		session.IsYolo = true;
		session.IsTerminalOpen = true;
		session.UserInput = "draft";
		session.PendingAttachments = attachments;
		session.UserInputResponseText = "response";

		session.Ui.IsYolo.ShouldBeTrue();
		session.Ui.IsTerminalOpen.ShouldBeTrue();
		session.Ui.DraftText.ShouldBe("draft");
		session.Ui.PendingAttachments.ShouldBeSameAs(attachments);
		session.Ui.UserInputResponseText.ShouldBe("response");
		(session.PendingAttachmentsLock == session.Ui.PendingAttachmentsLock).ShouldBeTrue();
	}

	[Fact]
	public void UiState_IsScopedToItsSession()
	{
		SessionModel first = CreateSession("first");
		SessionModel second = CreateSession("second");

		first.Ui.DraftText = "first draft";
		first.Ui.PendingAttachments.Add(
			new AttachmentModel("file.txt", "file.txt", "data:text/plain;base64,dGVzdA==", "text/plain"));

		second.Ui.DraftText.ShouldBeEmpty();
		second.Ui.PendingAttachments.ShouldBeEmpty();
		(first.Ui.PendingAttachmentsLock == second.Ui.PendingAttachmentsLock).ShouldBeFalse();
	}
}
