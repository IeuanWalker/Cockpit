using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Cockpit.Features.AppSettings;
using Cockpit.Features.Canvas;
using Cockpit.Features.Theme;
using Cockpit.UnitTests.Features.AppSettings;
using GitHub.Copilot;
using GitHub.Copilot.Rpc;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

#pragma warning disable GHCP001

namespace Cockpit.UnitTests.Features.Canvas;

/// <summary>
/// Tests for <see cref="CanvasWindowManager"/> pure-logic paths.
/// MAUI window operations (OpenWindow / CloseWindow) are bypassed by seeding
/// the internal dictionaries directly via reflection.
/// </summary>
public class CanvasWindowManagerTests
{
	static (CanvasWindowManager manager, ConcurrentDictionary<string, CanvasInstanceModel> instances, ConcurrentQueue<string> pendingIds) Create()
	{
		IAppSettingsFeature settings = new AppSettingsFeature(new UserAppSettings(new InMemoryPreferencesStorage()));
		ThemeStateFeature theme = new(settings);
		CanvasWindowManager manager = new(theme, NullLogger<CanvasWindowManager>.Instance);

		ConcurrentDictionary<string, CanvasInstanceModel> instances = (ConcurrentDictionary<string, CanvasInstanceModel>)
			typeof(CanvasWindowManager)
				.GetField("_instances", BindingFlags.NonPublic | BindingFlags.Instance)!
				.GetValue(manager)!;

		ConcurrentQueue<string> pendingIds = (ConcurrentQueue<string>)
			typeof(CanvasWindowManager)
				.GetField("_pendingInstanceIds", BindingFlags.NonPublic | BindingFlags.Instance)!
				.GetValue(manager)!;

		return (manager, instances, pendingIds);
	}

	static CanvasInstanceModel MakeInstance(string instanceId = "inst-1", string canvasId = "cockpit-canvas", string sessionId = "sess-1")
		=> new() { InstanceId = instanceId, CanvasId = canvasId, SessionId = sessionId };

	// -------------------------------------------------------------------------
	// ClaimPendingInstanceId
	// -------------------------------------------------------------------------

	[Fact]
	public void ClaimPendingInstanceId_ReturnsNull_WhenQueueIsEmpty()
	{
		(CanvasWindowManager manager, _, _) = Create();
		manager.ClaimPendingInstanceId().ShouldBeNull();
	}

	[Fact]
	public void ClaimPendingInstanceId_ReturnsId_AfterEnqueue()
	{
		(CanvasWindowManager manager, _, ConcurrentQueue<string> pending) = Create();
		pending.Enqueue("my-instance");
		manager.ClaimPendingInstanceId().ShouldBe("my-instance");
	}

	[Fact]
	public void ClaimPendingInstanceId_Dequeues_InFifoOrder()
	{
		(CanvasWindowManager manager, _, ConcurrentQueue<string> pending) = Create();
		pending.Enqueue("inst-A");
		pending.Enqueue("inst-B");

		manager.ClaimPendingInstanceId().ShouldBe("inst-A");
		manager.ClaimPendingInstanceId().ShouldBe("inst-B");
		manager.ClaimPendingInstanceId().ShouldBeNull();
	}

	// -------------------------------------------------------------------------
	// GetInstance
	// -------------------------------------------------------------------------

	[Fact]
	public void GetInstance_ReturnsNull_WhenNotRegistered()
	{
		(CanvasWindowManager manager, _, _) = Create();
		manager.GetInstance("unknown").ShouldBeNull();
	}

	[Fact]
	public void GetInstance_Returns_RegisteredInstance()
	{
		(CanvasWindowManager manager, ConcurrentDictionary<string, CanvasInstanceModel> instances, _) = Create();
		CanvasInstanceModel inst = MakeInstance("inst-1");
		instances["inst-1"] = inst;

		manager.GetInstance("inst-1").ShouldBe(inst);
	}

	// -------------------------------------------------------------------------
	// CloseAsync
	// -------------------------------------------------------------------------

	[Fact]
	public async Task CloseAsync_Succeeds_WhenInstanceNotFound()
	{
		(CanvasWindowManager manager, _, _) = Create();
		// Should not throw; no-op for unknown instance
		await manager.CloseAsync("nonexistent", CancellationToken.None);
	}

	[Fact]
	public async Task CloseAsync_RemovesInstance_WhenNoWindow()
	{
		(CanvasWindowManager manager, ConcurrentDictionary<string, CanvasInstanceModel> instances, _) = Create();
		CanvasInstanceModel inst = MakeInstance("inst-1");
		instances["inst-1"] = inst;

		await manager.CloseAsync("inst-1", CancellationToken.None);

		manager.GetInstance("inst-1").ShouldBeNull();
	}

	// -------------------------------------------------------------------------
	// InvokeActionAsync
	// -------------------------------------------------------------------------

	[Fact]
	public async Task InvokeActionAsync_ThrowsCanvasException_WhenInstanceUnknown()
	{
		(CanvasWindowManager manager, _, _) = Create();

		CanvasProviderInvokeActionRequest request = new()
		{
			SessionId = "sess-1",
			ExtensionId = "ext",
			CanvasId = "cockpit-canvas",
			InstanceId = "missing-inst",
			ActionName = "refresh"
		};

		await Should.ThrowAsync<CanvasException>(
			() => manager.InvokeActionAsync(request, CancellationToken.None));
	}

	[Fact]
	public async Task InvokeActionAsync_ThrowsCanvasException_WhenNoCallbackRegistered()
	{
		(CanvasWindowManager manager, ConcurrentDictionary<string, CanvasInstanceModel> instances, _) = Create();
		CanvasInstanceModel inst = MakeInstance();
		instances[inst.InstanceId] = inst;

		CanvasProviderInvokeActionRequest request = new()
		{
			SessionId = inst.SessionId,
			ExtensionId = "ext",
			CanvasId = inst.CanvasId,
			InstanceId = inst.InstanceId,
			ActionName = "refresh"
		};

		await Should.ThrowAsync<CanvasException>(
			() => manager.InvokeActionAsync(request, CancellationToken.None));
	}

	[Fact]
	public async Task InvokeActionAsync_InvokesCallback_WithCorrectArguments()
	{
		(CanvasWindowManager manager, ConcurrentDictionary<string, CanvasInstanceModel> instances, _) = Create();
		string? receivedAction = null;
		JsonElement? receivedInput = null;

		CanvasInstanceModel inst = MakeInstance();
		inst.ActionCallback = (action, input, _) =>
		{
			receivedAction = action;
			receivedInput = input;
			return Task.FromResult<object?>("ok");
		};
		instances[inst.InstanceId] = inst;

		JsonElement payload = JsonDocument.Parse("{\"key\":\"value\"}").RootElement;
		CanvasProviderInvokeActionRequest request = new()
		{
			SessionId = inst.SessionId,
			ExtensionId = "ext",
			CanvasId = inst.CanvasId,
			InstanceId = inst.InstanceId,
			ActionName = "do-thing",
			Input = payload
		};

		object? result = await manager.InvokeActionAsync(request, CancellationToken.None);

		receivedAction.ShouldBe("do-thing");
		receivedInput.HasValue.ShouldBeTrue();
		result.ShouldBe("ok");
	}

	// -------------------------------------------------------------------------
	// CloseAllForSessionAsync
	// -------------------------------------------------------------------------

	[Fact]
	public async Task CloseAllForSessionAsync_RemovesAllInstancesForSession()
	{
		(CanvasWindowManager manager, ConcurrentDictionary<string, CanvasInstanceModel> instances, _) = Create();
		instances["inst-A"] = MakeInstance("inst-A", sessionId: "sess-1");
		instances["inst-B"] = MakeInstance("inst-B", sessionId: "sess-1");
		instances["inst-C"] = MakeInstance("inst-C", sessionId: "sess-2");

		await manager.CloseAllForSessionAsync("sess-1");

		manager.GetInstance("inst-A").ShouldBeNull();
		manager.GetInstance("inst-B").ShouldBeNull();
		manager.GetInstance("inst-C").ShouldNotBeNull();
	}

	[Fact]
	public async Task CloseAllForSessionAsync_Succeeds_WhenNoInstancesForSession()
	{
		(CanvasWindowManager manager, _, _) = Create();
		// Should not throw
		await manager.CloseAllForSessionAsync("nonexistent-session");
	}

	// -------------------------------------------------------------------------
	// NotifyInstanceChanged
	// -------------------------------------------------------------------------

	[Fact]
	public void NotifyInstanceChanged_FiresEvent_WithCorrectId()
	{
		(CanvasWindowManager manager, _, _) = Create();
		string? received = null;
		manager.OnInstanceChanged += id => received = id;

		manager.NotifyInstanceChanged("inst-1");

		received.ShouldBe("inst-1");
	}

	[Fact]
	public void NotifyInstanceChanged_DoesNotThrow_WhenNoSubscribers()
	{
		(CanvasWindowManager manager, _, _) = Create();
		Should.NotThrow(() => manager.NotifyInstanceChanged("inst-1"));
	}
}
