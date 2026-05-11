using Cockpit.Features.Sessions;
using Cockpit.Features.Sessions.Models;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Cockpit.UnitTests.Features.Sessions;

public class SessionListFeatureTests
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
	public void AddSession_InsertsAtFront()
	{
		SessionListFeature feature = CreateFeature();
		SessionModel first = MakeSession("a");
		SessionModel second = MakeSession("b");

		feature.AddSession(first);
		feature.AddSession(second);

		feature.Sessions[0].Id.ShouldBe("b");
		feature.Sessions[1].Id.ShouldBe("a");
	}

	[Fact]
	public async Task SetCurrentSession_UpdatesCurrentAndFiresEvent()
	{
		SessionListFeature feature = CreateFeature();
		SessionModel session = MakeSession("x");
		feature.AddSession(session);

		bool eventFired = false;
		feature.OnStateChanged += () => eventFired = true;

		feature.SetCurrentSession(session);

		await Task.Delay(50, TestContext.Current.CancellationToken);

		feature.CurrentSession.ShouldBe(session);
		eventFired.ShouldBeTrue();
	}

	[Fact]
	public void RemoveSession_RemovesExistingSession()
	{
		SessionListFeature feature = CreateFeature();
		SessionModel session = MakeSession("del");
		feature.AddSession(session);

		feature.RemoveSession("del");

		feature.Sessions.ShouldBeEmpty();
	}

	[Fact]
	public async Task RemoveSession_FiresStateChanged_WhenSessionRemoved()
	{
		SessionListFeature feature = CreateFeature();
		SessionModel session = MakeSession("removed");
		feature.AddSession(session);
		int callCount = 0;
		feature.OnStateChanged += () => callCount++;

		feature.RemoveSession(session.Id);

		await Task.Delay(50, TestContext.Current.CancellationToken);

		callCount.ShouldBe(1);
	}

	[Fact]
	public void RemoveSession_NoOp_WhenSessionNotFound()
	{
		SessionListFeature feature = CreateFeature();
		feature.AddSession(MakeSession("a"));

		Should.NotThrow(() => feature.RemoveSession("nonexistent"));
		feature.Sessions.Count.ShouldBe(1);
	}

	[Fact]
	public void RemoveSession_SetsCurrentSessionNull_WhenCurrentDeleted()
	{
		SessionListFeature feature = CreateFeature();
		SessionModel first = MakeSession("first");
		SessionModel second = MakeSession("second");

		feature.AddSession(first);
		feature.AddSession(second);
		feature.SetCurrentSession(second);

		feature.RemoveSession("second");

		feature.CurrentSession.ShouldBeNull();
	}

	[Fact]
	public void RemoveSession_SetsCurrentToNull_WhenLastSessionDeleted()
	{
		SessionListFeature feature = CreateFeature();
		SessionModel session = MakeSession("only");
		feature.AddSession(session);
		feature.SetCurrentSession(session);

		feature.RemoveSession("only");

		feature.CurrentSession.ShouldBeNull();
		feature.Sessions.ShouldBeEmpty();
	}

	[Fact]
	public void RemoveSession_DoesNotChangeCurrentSession_WhenDifferentSessionDeleted()
	{
		SessionListFeature feature = CreateFeature();
		SessionModel kept = MakeSession("kept");
		SessionModel removed = MakeSession("removed");

		feature.AddSession(kept);
		feature.AddSession(removed);
		feature.SetCurrentSession(kept);

		feature.RemoveSession("removed");

		feature.CurrentSession.ShouldBe(kept);
		feature.Sessions.Count.ShouldBe(1);
	}

	[Fact]
	public async Task NotifyStateChanged_FiresOnStateChangedEvent()
	{
		SessionListFeature feature = CreateFeature();
		int callCount = 0;
		feature.OnStateChanged += () => callCount++;

		feature.NotifyStateChanged();
		feature.NotifyStateChanged();

		await Task.Delay(50, TestContext.Current.CancellationToken);

		// Two rapid calls are coalesced into a single notification
		callCount.ShouldBe(1);
	}

	[Fact]
	public async Task NotifyStateChanged_CanBeCalledMultipleTimes_WithoutThrowing()
	{
		SessionListFeature feature = CreateFeature();
		int callCount = 0;
		feature.OnStateChanged += () => callCount++;

		Should.NotThrow(() =>
		{
			for(int i = 0; i < 10; i++)
			{
				feature.NotifyStateChanged();
			}
		});

		await Task.Delay(100, TestContext.Current.CancellationToken);

		callCount.ShouldBe(1);
	}

	[Fact]
	public void ISessionStateProvider_GetSessions_ReturnsSessions()
	{
		SessionListFeature feature = CreateFeature();
		feature.AddSession(MakeSession("a"));
		feature.AddSession(MakeSession("b"));

		ISessionStateProvider provider = feature;
		provider.Sessions.Count.ShouldBe(2);
	}

	[Fact]
	public void ISessionStateProvider_CurrentSession_ReflectsCurrentSession()
	{
		SessionListFeature feature = CreateFeature();
		SessionModel session = MakeSession("current");
		feature.AddSession(session);
		feature.SetCurrentSession(session);

		ISessionStateProvider provider = feature;

		provider.CurrentSession.ShouldBe(session);
	}

	[Fact]
	public void AddSession_Multiple_SessionsOrderedNewestFirst()
	{
		SessionListFeature feature = CreateFeature();
		feature.AddSession(MakeSession("1", "First"));
		feature.AddSession(MakeSession("2", "Second"));
		feature.AddSession(MakeSession("3", "Third"));

		feature.Sessions[0].Id.ShouldBe("3");
		feature.Sessions[1].Id.ShouldBe("2");
		feature.Sessions[2].Id.ShouldBe("1");
	}
}
