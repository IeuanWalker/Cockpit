using Cockpit.Features.Sessions;
using Cockpit.Features.Sessions.Models;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Cockpit.UnitTests.Features.Sessions;

public class SessionFeatureLifecycleTests
{
	static readonly ModelInfo testModel = new() { Id = "test", Name = "Test Model" };

	static SessionModel MakeSession(string id, string title = "Test") => new()
	{
		Id = id,
		Title = title,
		Status = SessionStatusEnum.Idle,
		CreatedAt = DateTime.UtcNow,
		LastActivity = DateTime.UtcNow,
		Model = testModel,
		Context = new()
		{
			CurrentWorkingDirectory = "",
			WorkspacePath = null,
			GitRoot = null,
			Branch = null,
			Repository = null
		}
	};

	static SessionListFeature CreateFeature() => new(NullLogger<SessionListFeature>.Instance);

	[Fact]
	public void SessionListFeature_AddThenRemoveCurrent_CurrentSessionIsNull()
	{
		SessionListFeature feature = CreateFeature();
		SessionModel session = MakeSession("lifecycle-1");
		feature.AddSession(session);
		feature.SetCurrentSession(session);

		feature.RemoveSession(session.Id);

		feature.CurrentSession.ShouldBeNull();
	}

	[Fact]
	public void SessionListFeature_SetCurrentSession_CurrentSessionPropertyUpdated()
	{
		SessionListFeature feature = CreateFeature();
		SessionModel session = MakeSession("lifecycle-2");
		feature.AddSession(session);

		feature.SetCurrentSession(session);

		feature.CurrentSession.ShouldBe(session);
	}
}
