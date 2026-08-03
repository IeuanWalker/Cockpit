using Cockpit.Features.Sessions;
using Cockpit.Features.Sessions.Models;
using GitHub.Copilot;
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
		AgentRunState = AgentRunStateEnum.Idle,
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

	static async Task AssertNoNotificationAsync(Task notification)
	{
		Task timeout = Task.Delay(100, TestContext.Current.CancellationToken);
		Task completed = await Task.WhenAny(notification, timeout);

		completed.ShouldBe(timeout, "no additional notification should be raised after the coalesced callback");
		await timeout;
	}

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

		TaskCompletionSource fired = new(TaskCreationOptions.RunContinuationsAsynchronously);
		feature.OnStateChanged += () => fired.TrySetResult();

		feature.SetCurrentSession(session);

		await fired.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

		feature.CurrentSession.ShouldBe(session);
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

		TaskCompletionSource fired = new(TaskCreationOptions.RunContinuationsAsynchronously);
		feature.OnStateChanged += () => fired.TrySetResult();

		feature.RemoveSession(session.Id);

		await fired.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
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
		TaskCompletionSource firstNotification = new(TaskCreationOptions.RunContinuationsAsynchronously);
		TaskCompletionSource unexpectedNotification = new(TaskCreationOptions.RunContinuationsAsynchronously);
		feature.OnStateChanged += () =>
		{
			int count = Interlocked.Increment(ref callCount);
			if(count == 1)
			{
				firstNotification.TrySetResult();
			}
			else
			{
				unexpectedNotification.TrySetResult();
			}
		};

		feature.NotifyStateChanged();
		feature.NotifyStateChanged();

		await firstNotification.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
		await AssertNoNotificationAsync(unexpectedNotification.Task);

		// Two rapid calls are coalesced into a single notification
		callCount.ShouldBe(1);
	}

	[Fact]
	public async Task NotifyStateChanged_CanBeCalledMultipleTimes_WithoutThrowing()
	{
		SessionListFeature feature = CreateFeature();
		int callCount = 0;
		TaskCompletionSource firstNotification = new(TaskCreationOptions.RunContinuationsAsynchronously);
		TaskCompletionSource unexpectedNotification = new(TaskCreationOptions.RunContinuationsAsynchronously);
		feature.OnStateChanged += () =>
		{
			int count = Interlocked.Increment(ref callCount);
			if(count == 1)
			{
				firstNotification.TrySetResult();
			}
			else
			{
				unexpectedNotification.TrySetResult();
			}
		};

		Should.NotThrow(() =>
		{
			for(int i = 0; i < 10; i++)
			{
				feature.NotifyStateChanged();
			}
		});

		await firstNotification.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
		await AssertNoNotificationAsync(unexpectedNotification.Task);

		callCount.ShouldBe(1);
	}

	[Fact]
	public void ISessionStateProvider_GetSessions_ReturnsSessions()
	{
		SessionListFeature feature = CreateFeature();
		feature.AddSession(MakeSession("a"));
		feature.AddSession(MakeSession("b"));

#pragma warning disable CA1859 // Use concrete types when possible for improved performance
		ISessionStateProvider provider = feature;
#pragma warning restore CA1859 // Use concrete types when possible for improved performance
		provider.Sessions.Count.ShouldBe(2);
	}

	[Fact]
	public void ISessionStateProvider_CurrentSession_ReflectsCurrentSession()
	{
		SessionListFeature feature = CreateFeature();
		SessionModel session = MakeSession("current");
		feature.AddSession(session);
		feature.SetCurrentSession(session);

#pragma warning disable CA1859 // Use concrete types when possible for improved performance
		ISessionStateProvider provider = feature;
#pragma warning restore CA1859 // Use concrete types when possible for improved performance

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

	[Fact]
	public async Task SetCurrentSession_ToNull_ClearsCurrentAndFiresEvent()
	{
		SessionListFeature feature = CreateFeature();
		SessionModel session = MakeSession("x");
		feature.AddSession(session);
		TaskCompletionSource initialNotification = new(TaskCreationOptions.RunContinuationsAsynchronously);
		feature.OnStateChanged += () => initialNotification.TrySetResult();
		feature.SetCurrentSession(session);
		await initialNotification.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

		TaskCompletionSource eventFiredTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
		feature.OnStateChanged += () => eventFiredTcs.TrySetResult();

		feature.SetCurrentSession(null!);
		await eventFiredTcs.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

		feature.CurrentSession.ShouldBeNull();
	}

	[Fact]
	public void RemoveSession_OfMultiple_PreservesOrder()
	{
		SessionListFeature feature = CreateFeature();
		feature.AddSession(MakeSession("1", "First"));
		feature.AddSession(MakeSession("2", "Second"));
		feature.AddSession(MakeSession("3", "Third"));

		feature.RemoveSession("2");

		feature.Sessions.Count.ShouldBe(2);
		feature.Sessions[0].Id.ShouldBe("3");
		feature.Sessions[1].Id.ShouldBe("1");
	}

	[Fact]
	public async Task NotifyStateChanged_RapidBurst_CoalescesIntoSingleEvent()
	{
		SessionListFeature feature = CreateFeature();
		int callCount = 0;
		TaskCompletionSource firstNotification = new(TaskCreationOptions.RunContinuationsAsynchronously);
		TaskCompletionSource unexpectedNotification = new(TaskCreationOptions.RunContinuationsAsynchronously);
		feature.OnStateChanged += () =>
		{
			int count = Interlocked.Increment(ref callCount);
			if(count == 1)
			{
				firstNotification.TrySetResult();
			}
			else
			{
				unexpectedNotification.TrySetResult();
			}
		};

		for(int i = 0; i < 50; i++)
		{
			feature.NotifyStateChanged();
		}

		await firstNotification.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
		await AssertNoNotificationAsync(unexpectedNotification.Task);

		// The entire synchronous burst lands in the same coalescing interval.
		callCount.ShouldBe(1);
	}

	[Fact]
	public async Task NotifyStateChanged_AfterCoalesce_NewCallFiresAgain()
	{
		SessionListFeature feature = CreateFeature();
		int callCount = 0;
		TaskCompletionSource firstNotification = new(TaskCreationOptions.RunContinuationsAsynchronously);
		TaskCompletionSource secondNotification = new(TaskCreationOptions.RunContinuationsAsynchronously);
		feature.OnStateChanged += () =>
		{
			int count = Interlocked.Increment(ref callCount);
			if(count == 1)
			{
				firstNotification.TrySetResult();
			}
			else if(count == 2)
			{
				secondNotification.TrySetResult();
			}
		};

		feature.NotifyStateChanged();
		await firstNotification.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

		int afterFirst = callCount;
		afterFirst.ShouldBe(1);

		feature.NotifyStateChanged();
		await secondNotification.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

		callCount.ShouldBe(2);
	}

	[Fact]
	public async Task TypedNotifications_RapidChangesForSession_UnionFlags()
	{
		SessionListFeature feature = CreateFeature();
		TaskCompletionSource<SessionStateChange> fired = new(TaskCreationOptions.RunContinuationsAsynchronously);
		feature.OnSessionStateChanged += change => fired.TrySetResult(change);

		feature.NotifyStateChanged("session", SessionChangeKind.ConversationContent);
		feature.NotifyStateChanged("session", SessionChangeKind.SessionSummary);
		feature.NotifyStateChanged("session", SessionChangeKind.ConversationStructure);

		SessionStateChange change = await fired.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
		change.SessionId.ShouldBe("session");
		change.Kind.ShouldBe(
			SessionChangeKind.ConversationContent
			| SessionChangeKind.ConversationStructure
			| SessionChangeKind.SessionSummary);
	}

	[Fact]
	public async Task TypedNotifications_ReplayResetCoalescedWithFirstEvent_PreservesResetFlag()
	{
		SessionListFeature feature = CreateFeature();
		TaskCompletionSource<SessionStateChange> fired = new(TaskCreationOptions.RunContinuationsAsynchronously);
		feature.OnSessionStateChanged += change => fired.TrySetResult(change);

		feature.NotifyStateChanged(
			"session",
			SessionChangeKind.ConversationReset | SessionChangeKind.ConversationStructure);
		feature.NotifyStateChanged(
			"session",
			SessionChangeKind.ConversationContent | SessionChangeKind.ConversationStructure);

		SessionStateChange change = await fired.Task.WaitAsync(
			TimeSpan.FromSeconds(5),
			TestContext.Current.CancellationToken);

		change.SessionId.ShouldBe("session");
		(change.Kind & SessionChangeKind.ConversationReset).ShouldBe(SessionChangeKind.ConversationReset);
		(change.Kind & SessionChangeKind.ConversationStructure).ShouldBe(SessionChangeKind.ConversationStructure);
		(change.Kind & SessionChangeKind.ConversationContent).ShouldBe(SessionChangeKind.ConversationContent);
	}

	[Fact]
	public async Task TypedNotifications_SameSessionReloadResetCoalescesWithSwitchNotification()
	{
		SessionListFeature feature = CreateFeature();
		SessionModel session = MakeSession("session");
		feature.AddSession(session);
		TaskCompletionSource<SessionStateChange> fired = new(TaskCreationOptions.RunContinuationsAsynchronously);
		feature.OnSessionStateChanged += change => fired.TrySetResult(change);

		// Mirrors the successful load ordering: SwitchCurrentSessionAsync publishes first,
		// then the same-session history replacement publishes its explicit reset.
		feature.SetCurrentSession(session);
		feature.NotifyStateChanged(
			session.Id,
			SessionChangeKind.ConversationReset | SessionChangeKind.ConversationStructure);

		SessionStateChange change = await fired.Task.WaitAsync(
			TimeSpan.FromSeconds(5),
			TestContext.Current.CancellationToken);

		change.SessionId.ShouldBe(session.Id);
		change.Kind.ShouldBe(
			SessionChangeKind.CurrentSession
			| SessionChangeKind.ConversationReset
			| SessionChangeKind.ConversationStructure);
	}

	[Fact]
	public async Task TypedNotifications_ChangesForDifferentSessions_RemainDistinct()
	{
		SessionListFeature feature = CreateFeature();
		List<SessionStateChange> changes = [];
		TaskCompletionSource fired = new(TaskCreationOptions.RunContinuationsAsynchronously);
		feature.OnSessionStateChanged += change =>
		{
			lock(changes)
			{
				changes.Add(change);
				if(changes.Count == 2)
				{
					fired.TrySetResult();
				}
			}
		};

		feature.NotifyStateChanged("first", SessionChangeKind.ConversationContent);
		feature.NotifyStateChanged("second", SessionChangeKind.SessionSummary);

		await fired.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
		changes.ShouldContain(new SessionStateChange("first", SessionChangeKind.ConversationContent));
		changes.ShouldContain(new SessionStateChange("second", SessionChangeKind.SessionSummary));
	}
}
