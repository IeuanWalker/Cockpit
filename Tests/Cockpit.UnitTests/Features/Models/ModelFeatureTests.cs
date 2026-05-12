using Cockpit.Features.Models;
using Cockpit.Features.Sdk;
using Cockpit.Features.Sessions.Models;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Cockpit.UnitTests.Features.Models;

public sealed class ModelFeatureTests : IDisposable
{
	readonly string _tempDir;

	public ModelFeatureTests()
	{
		_tempDir = Path.Combine(Path.GetTempPath(), "CockpitModelTests_" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_tempDir);
	}

	public void Dispose()
	{
		if(Directory.Exists(_tempDir))
		{
			Directory.Delete(_tempDir, recursive: true);
		}
	}

	// ── Helpers ──────────────────────────────────────────────────────────────

	static ModelInfo MakeModel(string id, double? billingMultiplier = null) => new()
	{
		Id = id,
		Name = id,
		Billing = billingMultiplier is null ? null : new ModelBilling { Multiplier = billingMultiplier.Value }
	};

	static ModelFeature CreateFeature() => new(
		new CopilotClientFeature(NullLogger<CopilotClientFeature>.Instance),
		NullLogger<ModelFeature>.Instance);

	static SessionModel MakeSession(string? workspacePath = null) => new()
	{
		Id = Guid.NewGuid().ToString(),
		Title = "Test",
		Status = SessionStatusEnum.Active,
		CreatedAt = DateTime.UtcNow,
		LastActivity = DateTime.UtcNow,
		Model = MakeModel("default"),
		Context = new()
		{
			CurrentWorkingDirectory = workspacePath ?? string.Empty,
			WorkspacePath = workspacePath,
			GitRoot = null,
			Branch = null,
			Repository = null
		}
	};

	// ── SelectDefaultModel ────────────────────────────────────────────────────

	[Fact]
	public void SelectDefaultModel_EmptyList_Throws()
	{
		Should.Throw<InvalidOperationException>(() => ModelFeature.SelectDefaultModel([]));
	}

	[Fact]
	public void SelectDefaultModel_NoFreeModels_ReturnsFirstModel()
	{
		ModelInfo paid1 = MakeModel("paid1", 2.0);
		ModelInfo paid2 = MakeModel("paid2", 3.0);

		ModelInfo result = ModelFeature.SelectDefaultModel([paid1, paid2]);

		result.Id.ShouldBe("paid1");
	}

	[Fact]
	public void SelectDefaultModel_OneFreeModel_ReturnsThatModel()
	{
		ModelInfo free = MakeModel("free", 0);
		ModelInfo paid = MakeModel("paid", 2.0);

		ModelInfo result = ModelFeature.SelectDefaultModel([free, paid]);

		result.Id.ShouldBe("free");
	}

	[Fact]
	public void SelectDefaultModel_TwoFreeModels_ReturnsSecondFree()
	{
		ModelInfo free1 = MakeModel("free1", 0);
		ModelInfo free2 = MakeModel("free2", 0);
		ModelInfo paid = MakeModel("paid", 2.0);

		ModelInfo result = ModelFeature.SelectDefaultModel([free1, free2, paid]);

		result.Id.ShouldBe("free2");
	}

	[Fact]
	public void SelectDefaultModel_ThreeFreeModels_ReturnsSecondFree()
	{
		ModelInfo free1 = MakeModel("free1", 0);
		ModelInfo free2 = MakeModel("free2", 0);
		ModelInfo free3 = MakeModel("free3", 0);

		ModelInfo result = ModelFeature.SelectDefaultModel([free1, free2, free3]);

		result.Id.ShouldBe("free2");
	}

	[Fact]
	public void SelectDefaultModel_ModelWithNullBilling_CountedAsNonFree()
	{
		ModelInfo noBilling = MakeModel("noBilling", null);
		ModelInfo free = MakeModel("free", 0);

		// noBilling has null billing, so it is not a free model
		ModelInfo result = ModelFeature.SelectDefaultModel([noBilling, free]);

		result.Id.ShouldBe("free");
	}

	// ── SaveSessionModel / TryRestoreModelSettings ────────────────────────────

	[Fact]
	public async Task SaveSessionModel_NoWorkspacePath_DoesNotThrow()
	{
		ModelFeature feature = CreateFeature();
		SessionModel session = MakeSession(workspacePath: null);
		session.Model = MakeModel("gpt-4o", 1.0);

		await Should.NotThrowAsync(() => feature.SaveSessionModel(session));
	}

	[Fact]
	public async Task TryRestoreModelSettings_NoWorkspacePath_ReturnsFalse()
	{
		ModelFeature feature = CreateFeature();
		SessionModel session = MakeSession(workspacePath: null);

		bool result = await feature.TryRestoreModelSettings(session);

		result.ShouldBeFalse();
	}

	[Fact]
	public async Task TryRestoreModelSettings_FileDoesNotExist_ReturnsFalse()
	{
		ModelFeature feature = CreateFeature();
		SessionModel session = MakeSession(workspacePath: _tempDir);

		bool result = await feature.TryRestoreModelSettings(session);

		result.ShouldBeFalse();
	}

	[Fact]
	public async Task TryRestoreModelSettings_InvalidJson_ReturnsFalse()
	{
		ModelFeature feature = CreateFeature();
		SessionModel session = MakeSession(workspacePath: _tempDir);

		string settingsDir = Path.Combine(_tempDir, "Cockpit");
		Directory.CreateDirectory(settingsDir);
		await File.WriteAllTextAsync(Path.Combine(settingsDir, "session-model.json"), "not valid json {{{{", TestContext.Current.CancellationToken);

		bool result = await feature.TryRestoreModelSettings(session);

		result.ShouldBeFalse();
	}

	[Fact]
	public async Task TryRestoreModelSettings_MissingModelIdKey_ReturnsFalse()
	{
		ModelFeature feature = CreateFeature();
		SessionModel session = MakeSession(workspacePath: _tempDir);

		string settingsDir = Path.Combine(_tempDir, "Cockpit");
		Directory.CreateDirectory(settingsDir);
		// Valid JSON but no ModelId key
		await File.WriteAllTextAsync(Path.Combine(settingsDir, "session-model.json"), """{"ReasoningEffort":"medium"}""", TestContext.Current.CancellationToken);

		bool result = await feature.TryRestoreModelSettings(session);

		result.ShouldBeFalse();
	}

	[Fact]
	public async Task SaveAndRestore_RoundTrip_RestoresModelId()
	{
		ModelFeature feature = CreateFeature();
		ModelInfo savedModel = MakeModel("claude-3-5", 1.5);
		ModelInfo otherModel = MakeModel("gpt-4o", 1.0);

		SessionModel saveSession = MakeSession(workspacePath: _tempDir);
		saveSession.Model = savedModel;
		saveSession.ReasoningEffort = null;

		await feature.SaveSessionModel(saveSession);

		// Simulate a reload: inject cached models so TryRestoreModelSettings can
		// resolve the model by ID without touching the SDK.
		InjectCachedModels(feature, [savedModel, otherModel]);

		SessionModel restoreSession = MakeSession(workspacePath: _tempDir);
		restoreSession.Model = otherModel;

		bool restored = await feature.TryRestoreModelSettings(restoreSession);

		restored.ShouldBeTrue();
		restoreSession.Model.Id.ShouldBe("claude-3-5");
		restoreSession.ModelChanged.ShouldBeTrue();
	}

	[Fact]
	public async Task SaveAndRestore_SameModel_ModelChangedRemainsUnset()
	{
		ModelFeature feature = CreateFeature();
		ModelInfo model = MakeModel("gpt-4o", 1.0);

		SessionModel session = MakeSession(workspacePath: _tempDir);
		session.Model = model;
		await feature.SaveSessionModel(session);

		InjectCachedModels(feature, [model]);

		SessionModel restoreSession = MakeSession(workspacePath: _tempDir);
		restoreSession.Model = model; // already has the same model

		bool restored = await feature.TryRestoreModelSettings(restoreSession);

		restored.ShouldBeTrue();
		restoreSession.ModelChanged.ShouldBeFalse(); // no change → no restart needed
	}

	[Fact]
	public async Task SaveAndRestore_WithReasoningEffort_RestoresEffort()
	{
		ModelFeature feature = CreateFeature();
		ModelInfo model = new()
		{
			Id = "o3",
			Name = "o3",
			SupportedReasoningEfforts = ["low", "medium", "high"],
			DefaultReasoningEffort = "medium",
			Billing = new ModelBilling { Multiplier = 2.0 }
		};

		SessionModel saveSession = MakeSession(workspacePath: _tempDir);
		saveSession.Model = model;
		saveSession.ReasoningEffort = "high";
		await feature.SaveSessionModel(saveSession);

		InjectCachedModels(feature, [model]);

		SessionModel restoreSession = MakeSession(workspacePath: _tempDir);
		restoreSession.Model = model;
		restoreSession.ReasoningEffort = "low";

		bool restored = await feature.TryRestoreModelSettings(restoreSession);

		restored.ShouldBeTrue();
		restoreSession.ReasoningEffort.ShouldBe("high");
	}

	[Fact]
	public async Task TryRestoreModelSettings_ModelIdNotInAvailableModels_ReturnsFalse()
	{
		ModelFeature feature = CreateFeature();
		ModelInfo savedModel = MakeModel("deleted-model", 1.0);
		ModelInfo availableModel = MakeModel("gpt-4o", 1.0);

		SessionModel saveSession = MakeSession(workspacePath: _tempDir);
		saveSession.Model = savedModel;
		await feature.SaveSessionModel(saveSession);

		// Only inject a different model — saved model is no longer available.
		InjectCachedModels(feature, [availableModel]);

		SessionModel restoreSession = MakeSession(workspacePath: _tempDir);
		restoreSession.Model = availableModel;

		bool restored = await feature.TryRestoreModelSettings(restoreSession);

		restored.ShouldBeFalse();
	}

	[Fact]
	public async Task TryRestoreModelSettings_ModelHasNoSupportedEfforts_ClearsStaleReasoningEffort()
	{
		ModelFeature feature = CreateFeature();
		ModelInfo model = MakeModel("gpt-4o", 1.0); // no SupportedReasoningEfforts

		SessionModel saveSession = MakeSession(workspacePath: _tempDir);
		saveSession.Model = model;
		saveSession.ReasoningEffort = "high"; // stale effort from an old model
		await feature.SaveSessionModel(saveSession);

		InjectCachedModels(feature, [model]);

		SessionModel restoreSession = MakeSession(workspacePath: _tempDir);
		restoreSession.Model = model;
		restoreSession.ReasoningEffort = "high"; // stale — model doesn't support efforts

		bool restored = await feature.TryRestoreModelSettings(restoreSession);

		restored.ShouldBeTrue();
		restoreSession.ReasoningEffort.ShouldBeNull();
		restoreSession.ModelChanged.ShouldBeTrue(); // clearing stale effort signals a reload
	}

	[Fact]
	public async Task TryRestoreModelSettings_UnsupportedReasoningEffort_EffortNotRestored()
	{
		ModelFeature feature = CreateFeature();
		ModelInfo model = new()
		{
			Id = "o3",
			Name = "o3",
			SupportedReasoningEfforts = ["low", "medium"],
			DefaultReasoningEffort = "medium",
			Billing = new ModelBilling { Multiplier = 2.0 }
		};

		SessionModel saveSession = MakeSession(workspacePath: _tempDir);
		saveSession.Model = model;
		saveSession.ReasoningEffort = "ultra"; // effort not in SupportedReasoningEfforts
		await feature.SaveSessionModel(saveSession);

		InjectCachedModels(feature, [model]);

		SessionModel restoreSession = MakeSession(workspacePath: _tempDir);
		restoreSession.Model = model;
		restoreSession.ReasoningEffort = "low";

		bool restored = await feature.TryRestoreModelSettings(restoreSession);

		restored.ShouldBeTrue();
		restoreSession.ReasoningEffort.ShouldBe("low"); // unchanged — "ultra" not applied
		restoreSession.ModelChanged.ShouldBeFalse();
	}

	[Fact]
	public async Task SaveSessionModel_WritesReadableJson_ContainsModelId()
	{
		ModelFeature feature = CreateFeature();
		SessionModel session = MakeSession(workspacePath: _tempDir);
		session.Model = MakeModel("claude-3-5", 1.5);

		await feature.SaveSessionModel(session);

		string expectedPath = Path.Combine(_tempDir, "Cockpit", "session-model.json");
		File.Exists(expectedPath).ShouldBeTrue();

		string json = await File.ReadAllTextAsync(expectedPath, TestContext.Current.CancellationToken);
		json.ShouldContain("claude-3-5");
	}

	[Fact]
	public void SelectDefaultModel_SinglePaidModel_ReturnsThatModel()
	{
		ModelInfo paid = MakeModel("paid", 3.0);

		ModelInfo result = ModelFeature.SelectDefaultModel([paid]);

		result.Id.ShouldBe("paid");
	}

	/// <summary>
	/// Injects <paramref name="models"/> into the feature's private cache so that
	/// <see cref="ModelFeature.TryRestoreModelSettings"/> can resolve model IDs
	/// without calling the real Copilot SDK.
	/// </summary>
	static void InjectCachedModels(ModelFeature feature, IList<ModelInfo> models)
	{
		System.Reflection.FieldInfo? field = typeof(ModelFeature)
			.GetField("_models", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
		field.ShouldNotBeNull("Expected _models field to exist on ModelFeature");
		field!.SetValue(feature, models);
	}
}
