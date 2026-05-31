using Cockpit.Features.Permissions;
using Cockpit.Features.Permissions.Models;
using Cockpit.Features.Sessions;
using Cockpit.Features.Sessions.Models;
using GitHub.Copilot;
using GitHub.Copilot.Rpc;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Cockpit.UnitTests.Features.Permissions;

/// <summary>
/// Tests for <see cref="PermissionFeature"/> handler methods (PermissionFeature.HandleRequests.cs).
/// Each test invokes <see cref="PermissionFeature.HandlePermissionRequest"/> and captures the
/// produced <see cref="PermissionRequestModel"/> via the <see cref="PermissionFeature.OnPermissionRequested"/> event.
/// </summary>
public sealed class PermissionFeatureHandleRequestsTests : IDisposable
{
	static readonly ModelInfo testModel = new() { Id = "test", Name = "Test Model" };
	const string sessionId = "session1";
	const string workingDirectory = @"C:\projects\my-app";

	// Temp files created by helpers; xUnit will call Dispose on the test class instance.
	readonly List<string> _tempFiles = [];

	// ── Helpers ──────────────────────────────────────────────────────────────

	(PermissionFeature Feature, GlobalPermissionFeature GlobalPermissions) CreateFeature(
		string cwd = workingDirectory,
		Action<GlobalDenyFeature>? configureDeny = null)
	{
		string globalFile = Path.Combine(Path.GetTempPath(), $"test-{Guid.NewGuid()}.json");
		string denyFile = Path.Combine(Path.GetTempPath(), $"deny-{Guid.NewGuid()}.json");

		GlobalPermissionFeature globalPermissions = new(NullLogger<GlobalPermissionFeature>.Instance, globalFile);
		GlobalDenyFeature denyFeature = new(NullLogger<GlobalDenyFeature>.Instance, denyFile);
		configureDeny?.Invoke(denyFeature);

		// Track created temp files so xUnit can clean them up via Dispose.
		_tempFiles.Add(globalFile);
		_tempFiles.Add(denyFile);

		TestSessionStateProvider stateProvider = new();
		stateProvider.AddSession(new SessionModel
		{
			Id = sessionId,
			Title = "Test",
			CreatedAt = DateTime.UtcNow,
			LastActivity = DateTime.UtcNow,
			Model = testModel,
			Context = new() { CurrentWorkingDirectory = cwd, WorkspacePath = null, GitRoot = null, Branch = null, Repository = null }
		});

		SessionPermissionFeature sessionPermissions = new(stateProvider);
		PermissionFeature feature = new(globalPermissions, denyFeature, sessionPermissions, stateProvider, NullLogger<PermissionFeature>.Instance)
		{
			ThrowOnUnhandledPermissionType = true
		};
		return (feature, globalPermissions);
	}

	/// <summary>Calls HandlePermissionRequest and captures the PermissionRequestModel via the event.</summary>
	static async Task<PermissionRequestModel> CaptureModelAsync(PermissionFeature feature, PermissionRequest sdkRequest)
	{
		PermissionRequestModel? captured = null;
		feature.OnPermissionRequested += (_, model) =>
		{
			captured = model;
			feature.ResolvePermissionRequest(model.Id, PermissionDecisionEnum.Once);
		};

		await feature.HandlePermissionRequest(sdkRequest, new PermissionInvocation { SessionId = sessionId })
			.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

		captured.ShouldNotBeNull("Expected OnPermissionRequested to fire but it did not.");
		return captured;
	}

	// ── Shell ─────────────────────────────────────────────────────────────────

	[Fact]
	public async Task HandleShell_SimpleCommand_ExtractsExecutableAndBuildsTitle()
	{
		(PermissionFeature feature, _) = CreateFeature();

		PermissionRequestModel model = await CaptureModelAsync(feature, new PermissionRequestShell
		{
			FullCommandText = "docker run --rm ubuntu",
			Intention = "Run container",
			Commands = [],
			PossiblePaths = [],
			PossibleUrls = [],
			HasWriteFileRedirection = false,
			CanOfferSessionApproval = true
		});

		model.Commands.ShouldNotBeEmpty();
		model.Commands.ShouldAllBe(c => c.Contains("docker", StringComparison.OrdinalIgnoreCase));
		model.RequestTitle.ShouldContain("docker");
		model.Intention.ShouldBe("Run container");
		model.IsDestructive.ShouldBeFalse();
		model.CanApproveGlobally.ShouldBeTrue();
	}

	[Fact]
	public async Task HandleShell_DestructiveCommand_SetsIsDestructiveAndWarningTitle()
	{
		(PermissionFeature feature, _) = CreateFeature();

		PermissionRequestModel model = await CaptureModelAsync(feature, new PermissionRequestShell
		{
			FullCommandText = "rm -rf ./dist",
			Intention = string.Empty,
			Commands = [],
			PossiblePaths = [],
			PossibleUrls = [],
			HasWriteFileRedirection = false,
			CanOfferSessionApproval = false
		});

		model.IsDestructive.ShouldBeTrue();
		model.RequestTitle.ShouldStartWith("⚠️");
		model.CanApproveGlobally.ShouldBeFalse();
	}

	[Fact]
	public async Task HandleShell_CommandWithFileDeletion_IncludesFileCountInTitle()
	{
		(PermissionFeature feature, _) = CreateFeature();

		PermissionRequestModel model = await CaptureModelAsync(feature, new PermissionRequestShell
		{
			FullCommandText = "rm file1.txt file2.txt",
			Intention = string.Empty,
			Commands = [],
			PossiblePaths = [],
			PossibleUrls = [],
			HasWriteFileRedirection = false,
			CanOfferSessionApproval = false
		});

		model.IsDestructive.ShouldBeTrue();
		model.FilesToDelete.Count.ShouldBeGreaterThan(0);
		model.RequestTitle.ShouldContain("file(s)");
	}

	[Fact]
	public async Task HandleShell_DeniedCommand_CannotApproveGlobally()
	{
		(PermissionFeature feature, _) = CreateFeature(configureDeny: deny => deny.Add("curl"));

		PermissionRequestModel model = await CaptureModelAsync(feature, new PermissionRequestShell
		{
			FullCommandText = "curl https://example.com",
			Intention = string.Empty,
			Commands = [],
			PossiblePaths = [],
			PossibleUrls = [],
			HasWriteFileRedirection = false,
			CanOfferSessionApproval = true
		});

		model.CanApproveGlobally.ShouldBeFalse();
	}

	// ── Write ─────────────────────────────────────────────────────────────────

	[Fact]
	public async Task HandleWrite_FileInWorkingDirectory_CanApproveGlobally()
	{
		(PermissionFeature feature, _) = CreateFeature();

		PermissionRequestModel model = await CaptureModelAsync(feature, new PermissionRequestWrite
		{
			FileName = workingDirectory + @"\src\index.ts",
			Intention = "Update source",
			Diff = string.Empty,
			CanOfferSessionApproval = true
		});

		model.CanApproveGlobally.ShouldBeTrue();
		model.RequestTitle.ShouldContain("current working directory");
		model.Intention.ShouldBe("Update source");
	}

	[Fact]
	public async Task HandleWrite_FileOutsideWorkingDirectory_CannotApproveGlobally()
	{
		(PermissionFeature feature, _) = CreateFeature();

		PermissionRequestModel model = await CaptureModelAsync(feature, new PermissionRequestWrite
		{
			FileName = @"C:\other-project\secrets.json",
			Intention = string.Empty,
			Diff = string.Empty,
			CanOfferSessionApproval = true
		});

		model.CanApproveGlobally.ShouldBeFalse();
		model.RequestTitle.ShouldContain("outside of current working directory");
	}

	[Fact]
	public async Task HandleWrite_CopilotSessionFile_CanApproveGlobally()
	{
		(PermissionFeature feature, _) = CreateFeature();

		PermissionRequestModel model = await CaptureModelAsync(feature, new PermissionRequestWrite
		{
			FileName = @"C:\anywhere\.copilot\session.json",
			Intention = string.Empty,
			Diff = string.Empty,
			CanOfferSessionApproval = true
		});

		model.CanApproveGlobally.ShouldBeTrue();
		model.RequestTitle.ShouldContain("copilot session file");
	}

	// ── Read ──────────────────────────────────────────────────────────────────

	[Fact]
	public async Task HandleRead_FileInWorkingDirectory_CanApproveGlobally()
	{
		(PermissionFeature feature, _) = CreateFeature();

		PermissionRequestModel model = await CaptureModelAsync(feature, new PermissionRequestRead
		{
			Path = workingDirectory + @"\README.md",
			Intention = "Read docs"
		});

		model.CanApproveGlobally.ShouldBeTrue();
		model.RequestTitle.ShouldContain("current working directory");
	}

	[Fact]
	public async Task HandleRead_FileOutsideWorkingDirectory_CannotApproveGlobally()
	{
		(PermissionFeature feature, _) = CreateFeature();

		PermissionRequestModel model = await CaptureModelAsync(feature, new PermissionRequestRead
		{
			Path = @"C:\Windows\System32\config",
			Intention = string.Empty
		});

		model.CanApproveGlobally.ShouldBeFalse();
		model.RequestTitle.ShouldContain("outside of current working directory");
	}

	[Fact]
	public async Task HandleRead_CopilotSessionFile_CanApproveGlobally()
	{
		(PermissionFeature feature, _) = CreateFeature();

		PermissionRequestModel model = await CaptureModelAsync(feature, new PermissionRequestRead
		{
			Path = @"C:\anywhere\.copilot\context.json",
			Intention = string.Empty
		});

		model.CanApproveGlobally.ShouldBeTrue();
		model.RequestTitle.ShouldContain("copilot session file");
	}

	// ── MCP ───────────────────────────────────────────────────────────────────

	[Fact]
	public async Task HandleMcp_ReadOnly_CanApproveGlobally()
	{
		(PermissionFeature feature, _) = CreateFeature();

		PermissionRequestModel model = await CaptureModelAsync(feature, new PermissionRequestMcp
		{
			ServerName = "my-server",
			ToolName = "search",
			ToolTitle = "Search",
			ReadOnly = true
		});

		model.CanApproveGlobally.ShouldBeTrue();
	}

	[Fact]
	public async Task HandleMcp_NotReadOnly_CannotApproveGlobally()
	{
		(PermissionFeature feature, _) = CreateFeature();

		PermissionRequestModel model = await CaptureModelAsync(feature, new PermissionRequestMcp
		{
			ServerName = "my-server",
			ToolName = "write-db",
			ToolTitle = string.Empty,
			ReadOnly = false
		});

		model.CanApproveGlobally.ShouldBeFalse();
	}

	[Fact]
	public async Task HandleMcp_WithToolTitle_UsesToolTitleInRequestTitle()
	{
		(PermissionFeature feature, _) = CreateFeature();

		PermissionRequestModel model = await CaptureModelAsync(feature, new PermissionRequestMcp
		{
			ServerName = "my-server",
			ToolName = "internal-id",
			ToolTitle = "Friendly Name",
			ReadOnly = true
		});

		model.RequestTitle.ShouldContain("Friendly Name");
		model.RequestTitle.ShouldNotContain("internal-id");
	}

	[Fact]
	public async Task HandleMcp_WithoutToolTitle_FallsBackToToolName()
	{
		(PermissionFeature feature, _) = CreateFeature();

		PermissionRequestModel model = await CaptureModelAsync(feature, new PermissionRequestMcp
		{
			ServerName = "my-server",
			ToolName = "my-tool",
			ToolTitle = string.Empty,
			ReadOnly = true
		});

		model.RequestTitle.ShouldContain("my-tool");
	}

	[Fact]
	public async Task HandleMcp_CommandKey_CombinesServerAndToolName()
	{
		(PermissionFeature feature, _) = CreateFeature();

		PermissionRequestModel model = await CaptureModelAsync(feature, new PermissionRequestMcp
		{
			ServerName = "my-server",
			ToolName = "my-tool",
			ToolTitle = string.Empty,
			ReadOnly = true
		});

		model.Commands.ShouldBe(["my-server/my-tool"]);
	}

	// ── Memory ────────────────────────────────────────────────────────────────

	[Fact]
	public async Task HandleMemory_WithSubject_ShowsSubjectInTitle()
	{
		(PermissionFeature feature, _) = CreateFeature();

		PermissionRequestModel model = await CaptureModelAsync(feature, new PermissionRequestMemory
		{
			Subject = "User preference",
			Fact = "Likes dark mode",
			Citations = string.Empty
		});

		model.RequestTitle.ShouldContain("User preference");
		model.Commands.ShouldBe(["memory"]);
	}

	[Fact]
	public async Task HandleMemory_WithoutSubject_UsesGenericTitle()
	{
		(PermissionFeature feature, _) = CreateFeature();

		PermissionRequestModel model = await CaptureModelAsync(feature, new PermissionRequestMemory
		{
			Subject = string.Empty,
			Fact = "Some fact",
			Citations = string.Empty
		});

		model.RequestTitle.ShouldBe("Allow saving memory?");
	}

	// ── CustomTool ────────────────────────────────────────────────────────────

	[Fact]
	public async Task HandleCustomTool_ShowsToolNameInTitleAndCommandKey()
	{
		(PermissionFeature feature, _) = CreateFeature();

		PermissionRequestModel model = await CaptureModelAsync(feature, new PermissionRequestCustomTool
		{
			ToolName = "analyze-code",
			ToolDescription = "Analyses code"
		});

		model.RequestTitle.ShouldContain("analyze-code");
		model.Commands.ShouldBe(["custom:analyze-code"]);
		model.CanApproveGlobally.ShouldBeTrue();
	}

	// ── Hook ──────────────────────────────────────────────────────────────────

	[Fact]
	public async Task HandleHook_WithMessage_UsesMessageAsTitle()
	{
		(PermissionFeature feature, _) = CreateFeature();

		PermissionRequestModel model = await CaptureModelAsync(feature, new PermissionRequestHook
		{
			ToolName = "pre-commit",
			HookMessage = "Run pre-commit checks?"
		});

		model.RequestTitle.ShouldBe("Run pre-commit checks?");
		model.Commands.ShouldBe(["hook:pre-commit"]);
	}

	[Fact]
	public async Task HandleHook_WithoutMessage_UsesToolNameInTitle()
	{
		(PermissionFeature feature, _) = CreateFeature();

		PermissionRequestModel model = await CaptureModelAsync(feature, new PermissionRequestHook
		{
			ToolName = "post-build",
			HookMessage = string.Empty
		});

		model.RequestTitle.ShouldContain("post-build");
	}

	[Fact]
	public async Task HandleHook_CanApproveGlobally()
	{
		(PermissionFeature feature, _) = CreateFeature();

		PermissionRequestModel model = await CaptureModelAsync(feature, new PermissionRequestHook
		{
			ToolName = "pre-commit",
			HookMessage = string.Empty
		});

		model.CanApproveGlobally.ShouldBeTrue();
	}

	// ── All SDK types (auto-discovered) ──────────────────────────────────────

	/// <summary>
	/// Minimal valid instances for each SDK-registered <see cref="PermissionRequest"/> derived type.
	/// If the SDK registers a new type via <see cref="System.Text.Json.Serialization.JsonDerivedTypeAttribute"/>
	/// and it is not present here, <see cref="ToRequestModel_AllSdkRegisteredTypes_ProducesValidModel"/>
	/// will automatically fail with a clear, actionable message.
	/// </summary>
	static readonly Dictionary<Type, PermissionRequest> minimalFixtures = new()
	{
		[typeof(PermissionRequestShell)] = new PermissionRequestShell
		{
			FullCommandText = "git status",
			Intention = "Check status",
			Commands = [],
			PossiblePaths = [],
			PossibleUrls = [],
			HasWriteFileRedirection = false,
			CanOfferSessionApproval = true
		},
		[typeof(PermissionRequestWrite)] = new PermissionRequestWrite
		{
			FileName = workingDirectory + @"\src\index.ts",
			Intention = "Write file",
			Diff = string.Empty,
			CanOfferSessionApproval = true
		},
		[typeof(PermissionRequestRead)] = new PermissionRequestRead
		{
			Path = workingDirectory + @"\README.md",
			Intention = "Read file"
		},
		[typeof(PermissionRequestMcp)] = new PermissionRequestMcp
		{
			ServerName = "srv",
			ToolName = "tool",
			ToolTitle = string.Empty,
			ReadOnly = true
		},
		[typeof(PermissionRequestUrl)] = new PermissionRequestUrl
		{
			Url = "https://example.com",
			Intention = "Access URL"
		},
		[typeof(PermissionRequestMemory)] = new PermissionRequestMemory
		{
			Subject = "pref",
			Fact = "likes dark mode",
			Citations = string.Empty
		},
		[typeof(PermissionRequestCustomTool)] = new PermissionRequestCustomTool
		{
			ToolName = "my-tool",
			ToolDescription = "Does things"
		},
		[typeof(PermissionRequestHook)] = new PermissionRequestHook
		{
			ToolName = "pre-commit",
			HookMessage = string.Empty
		},
		[typeof(PermissionRequestExtensionManagement)] = new PermissionRequestExtensionManagement
		{
			ExtensionName = "my-extension",
			Operation = "scaffold",
			ToolCallId = "tc1"
		},
		[typeof(PermissionRequestExtensionPermissionAccess)] = new PermissionRequestExtensionPermissionAccess
		{
			ExtensionName = "my-extension",
			Capabilities = [],
			ToolCallId = "tc1"
		},
	};

	/// <summary>
	/// Reflects on <see cref="PermissionRequest"/> <see cref="System.Text.Json.Serialization.JsonDerivedTypeAttribute"/>
	/// attributes to enumerate every SDK-registered derived type.
	/// Yields <c>null</c> for any type not present in <see cref="minimalFixtures"/> so the theory
	/// body can fail with a descriptive message.
	/// </summary>
	public static TheoryData<string> AllSdkPermissionRequestTypes_Typed()
	{
		TheoryData<string> data = [];
		IEnumerable<Type> sdkTypes = typeof(PermissionRequest)
			.GetCustomAttributes(typeof(System.Text.Json.Serialization.JsonDerivedTypeAttribute), inherit: false)
			.Cast<System.Text.Json.Serialization.JsonDerivedTypeAttribute>()
			.Select(a => a.DerivedType);

		foreach(Type type in sdkTypes)
		{
			data.Add(type.Name);
		}

		return data;
	}

	[Theory]
	[MemberData(nameof(AllSdkPermissionRequestTypes_Typed))]
	public async Task ToRequestModel_AllSdkRegisteredTypes_ProducesValidModel(string typeName)
	{
		IEnumerable<Type> sdkTypes = typeof(PermissionRequest)
			.GetCustomAttributes(typeof(System.Text.Json.Serialization.JsonDerivedTypeAttribute), inherit: false)
			.Cast<System.Text.Json.Serialization.JsonDerivedTypeAttribute>()
			.Select(a => a.DerivedType);

		Type? targetType = sdkTypes.FirstOrDefault(t => t.Name == typeName);
		PermissionRequest? request = null;
		if(targetType is not null)
		{
			minimalFixtures.TryGetValue(targetType, out request);
		}
		request.ShouldNotBeNull(
			$"SDK type '{typeName}' is registered via [JsonDerivedType] but has no entry in {nameof(minimalFixtures)}. " +
			$"Add a minimal valid instance to keep handler coverage up to date.");

		(PermissionFeature feature, _) = CreateFeature();

		PermissionRequestModel? captured = null;
		feature.OnPermissionRequested += (_, model) =>
		{
			captured = model;
			feature.ResolvePermissionRequest(model.Id, PermissionDecisionEnum.Once);
		};

#pragma warning disable GHCP001
		PermissionDecision result = await feature.HandlePermissionRequest(request, new PermissionInvocation { SessionId = "session1" })
			.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
#pragma warning restore GHCP001

		// If ToRequestModel hits the default case with ThrowOnUnhandledPermissionType=true, the exception is
		// caught inside HandlePermissionRequest and returns DeniedCouldNotRequestFromUser instead of firing
		// OnPermissionRequested.
		result.GetType().Name.ShouldNotBe("PermissionDecisionUserNotAvailable",
			$"SDK type '{typeName}' hit the unhandled default case in ToRequestModel. " +
			$"Add a switch case for it in PermissionFeature.HandleRequests.cs.");

		captured.ShouldNotBeNull($"{typeName}: Expected OnPermissionRequested to fire but it did not.");
		captured.RequestTitle.ShouldNotBeNullOrWhiteSpace($"{typeName}: RequestTitle must not be blank");
		captured.Commands.ShouldNotBeEmpty($"{typeName}: Commands must not be empty");
		captured.Commands.ShouldAllBe(
			c => !string.IsNullOrWhiteSpace(c),
			$"{typeName}: Commands must not contain blank entries");
	}

	// ── URL ───────────────────────────────────────────────────────────────────

	[Fact]
	public async Task HandleUrl_WithUrl_ShowsUrlInTitle()
	{
		(PermissionFeature feature, _) = CreateFeature();

		PermissionRequestModel model = await CaptureModelAsync(feature, new PermissionRequestUrl
		{
			Url = "https://api.example.com/data",
			Intention = "Fetch data"
		});

		model.RequestTitle.ShouldContain("https://api.example.com/data");
		model.Commands.ShouldBe(["url"]);
		model.Intention.ShouldBe("Fetch data");
	}

	[Fact]
	public async Task HandleUrl_WithoutUrl_UsesGenericTitle()
	{
		(PermissionFeature feature, _) = CreateFeature();

		PermissionRequestModel model = await CaptureModelAsync(feature, new PermissionRequestUrl
		{
			Url = string.Empty,
			Intention = string.Empty
		});

		model.RequestTitle.ShouldBe("Allow URL access?");
	}

	public void Dispose()
	{
		Dispose(true);
		GC.SuppressFinalize(this);
	}

	void Dispose(bool disposing)
	{
		if(!disposing)
		{
			return;
		}


		foreach(string f in _tempFiles)
		{
			try
			{
				if(File.Exists(f))
				{
					File.Delete(f);
				}
			}
			catch { }
		}
	}

	// ── Helper types ──────────────────────────────────────────────────────────

	class TestSessionStateProvider : ISessionStateProvider
	{
		readonly List<SessionModel> _sessions = [];

		public void AddSession(SessionModel session) => _sessions.Add(session);
		public IReadOnlyList<SessionModel> Sessions => _sessions;
		public SessionModel? CurrentSession => _sessions.FirstOrDefault();
		public void NotifyStateChanged() => OnStateChanged?.Invoke();
		public event Action? OnStateChanged;
	}
}
