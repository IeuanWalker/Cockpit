using Cockpit.Features.Sessions.Models;
using Cockpit.Features.SlashCommands;
using GitHub.Copilot.SDK;
using Shouldly;

namespace Cockpit.UnitTests.Features.SlashCommands;

public class SlashCommandFeatureTests
{
	static SessionModel CreateSession() => new()
	{
		Id = "session-123",
		Title = "Slash Command Session",
		CreatedAt = DateTime.UtcNow,
		LastActivity = DateTime.UtcNow,
		Status = SessionStatusEnum.Idle,
		Messages = [],
		Model = new ModelInfo { Id = "gpt-5", Name = "GPT-5" },
		Context = new SessionContext
		{
			CurrentWorkingDirectory = "/repo",
			WorkspacePath = "/workspace",
			GitRoot = "/repo/.git",
			Repository = "IeuanWalker/Cockpit",
			Branch = "main"
		}
	};

	[Fact]
	public void TryHandle_ReturnsFalse_ForNormalChatInput()
	{
		SlashCommandFeature feature = new();
		SessionModel session = CreateSession();

		bool handled = feature.TryHandle(session, "hello world", out SlashCommandResult? result);

		handled.ShouldBeFalse();
		result.ShouldBeNull();
	}

	[Fact]
	public void TryHandle_ReturnsHelpCommandOutput()
	{
		SlashCommandFeature feature = new();
		SessionModel session = CreateSession();

		bool handled = feature.TryHandle(session, "/help", out SlashCommandResult? result);

		handled.ShouldBeTrue();
		result.ShouldNotBeNull();
		result.Success.ShouldBeTrue();
		result.Message.ShouldContain("/help");
		result.Message.ShouldContain("/session");
	}

	[Fact]
	public void TryHandle_ReturnsUnknownCommandError()
	{
		SlashCommandFeature feature = new();
		SessionModel session = CreateSession();

		bool handled = feature.TryHandle(session, "/wat", out SlashCommandResult? result);

		handled.ShouldBeTrue();
		result.ShouldNotBeNull();
		result.Success.ShouldBeFalse();
		result.Message.ShouldContain("Unknown command '/wat'");
	}

	[Fact]
	public void TryHandle_ReturnsSessionSummary()
	{
		SlashCommandFeature feature = new();
		SessionModel session = CreateSession();

		bool handled = feature.TryHandle(session, "/session", out SlashCommandResult? result);

		handled.ShouldBeTrue();
		result.ShouldNotBeNull();
		result.Success.ShouldBeTrue();
		result.Message.ShouldContain("session-123");
		result.Message.ShouldContain("gpt-5");
		result.Message.ShouldContain("/repo");
	}

	[Fact]
	public void TryHandle_ParsesQuotedArguments_ForUsageValidation()
	{
		SlashCommandFeature feature = new();
		SessionModel session = CreateSession();

		bool handled = feature.TryHandle(session, "/session \"extra argument\"", out SlashCommandResult? result);

		handled.ShouldBeTrue();
		result.ShouldNotBeNull();
		result.Success.ShouldBeFalse();
		result.Message.ShouldBe("Usage: /session");
	}
}
