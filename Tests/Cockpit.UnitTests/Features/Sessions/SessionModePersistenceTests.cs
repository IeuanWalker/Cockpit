using Cockpit.Features.Sessions;
using Cockpit.Features.Sessions.Models;
using GitHub.Copilot;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Cockpit.UnitTests.Features.Sessions;

public sealed class SessionModePersistenceTests : IDisposable
{
	readonly string _tempDir;

	public SessionModePersistenceTests()
	{
		_tempDir = Path.Combine(Path.GetTempPath(), $"CockpitTests_{Guid.NewGuid():N}");
		Directory.CreateDirectory(_tempDir);
	}

	public void Dispose()
	{
		if(Directory.Exists(_tempDir))
		{
			Directory.Delete(_tempDir, true);
		}
	}

	static readonly ModelInfo testModel = new() { Id = "test", Name = "Test Model" };

	SessionModel MakeSession(string id, SessionAgentModeEnum mode = SessionAgentModeEnum.Interactive)
	{
		SessionModel session = new()
		{
			Id = id,
			Title = "Test",
			CreatedAt = DateTime.UtcNow,
			LastActivity = DateTime.UtcNow,
			Model = testModel,
			Context = new()
			{
				CurrentWorkingDirectory = _tempDir,
				WorkspacePath = _tempDir,
				GitRoot = null,
				Branch = null,
				Repository = null
			}
		};
		session.Context.SelectedAgentMode = mode;
		return session;
	}

	static SessionModePersistence CreatePersistence() =>
		new(NullLogger<SessionModePersistence>.Instance);

	[Fact]
	public async Task SaveAndRestore_RoundTrips_Interactive()
	{
		SessionModePersistence persistence = CreatePersistence();
		SessionModel session = MakeSession("s1", SessionAgentModeEnum.Interactive);

		await persistence.SaveSessionMode(session, TestContext.Current.CancellationToken);

		SessionModel restored = MakeSession("s1");
		bool ok = await persistence.TryRestoreSessionMode(restored, TestContext.Current.CancellationToken);

		ok.ShouldBeTrue();
		restored.Context.SelectedAgentMode.ShouldBe(SessionAgentModeEnum.Interactive);
	}

	[Fact]
	public async Task SaveSessionModeAsync_PassesCancellationToken_DoesNotThrowOnValidMode()
	{
		SessionModePersistence persistence = CreatePersistence();
		SessionModel session = MakeSession("s-token", SessionAgentModeEnum.Plan);
		using CancellationTokenSource cts = new();

		await Should.NotThrowAsync(() => persistence.SaveSessionMode(session, cts.Token));
	}

	[Theory]
	[InlineData(SessionAgentModeEnum.Plan)]
	[InlineData(SessionAgentModeEnum.Autopilot)]
	public async Task SaveAndRestore_RoundTrips_NonDefaultModes(SessionAgentModeEnum mode)
	{
		SessionModePersistence persistence = CreatePersistence();
		SessionModel session = MakeSession("s2", mode);

		await persistence.SaveSessionMode(session, TestContext.Current.CancellationToken);

		SessionModel restored = MakeSession("s2");
		bool ok = await persistence.TryRestoreSessionMode(restored, TestContext.Current.CancellationToken);

		ok.ShouldBeTrue();
		restored.Context.SelectedAgentMode.ShouldBe(mode);
	}

	[Fact]
	public async Task TryRestoreSessionModeAsync_ReturnsFalse_WhenFileAbsent()
	{
		SessionModePersistence persistence = CreatePersistence();
		SessionModel session = MakeSession("s3");

		bool ok = await persistence.TryRestoreSessionMode(session, TestContext.Current.CancellationToken);

		ok.ShouldBeFalse();
	}

	[Fact]
	public async Task TryRestoreSessionModeAsync_ReturnsFalse_WhenCorruptJson()
	{
		SessionModePersistence persistence = CreatePersistence();
		SessionModel session = MakeSession("s4");

		string dir = Path.Combine(_tempDir, "Cockpit");
		Directory.CreateDirectory(dir);
		await File.WriteAllTextAsync(Path.Combine(dir, "session-agentmode.json"), "not-valid-json{{{{", TestContext.Current.CancellationToken);

		bool ok = await persistence.TryRestoreSessionMode(session, TestContext.Current.CancellationToken);

		ok.ShouldBeFalse();
	}

	[Fact]
	public async Task TryRestoreSessionModeAsync_ReturnsFalse_WhenJsonMissingAgentModeKey()
	{
		SessionModePersistence persistence = CreatePersistence();
		SessionModel session = MakeSession("s-no-agent-mode");

		string dir = Path.Combine(_tempDir, "Cockpit");
		Directory.CreateDirectory(dir);
		await File.WriteAllTextAsync(Path.Combine(dir, "session-agentmode.json"), "{}", TestContext.Current.CancellationToken);

		bool ok = await persistence.TryRestoreSessionMode(session, TestContext.Current.CancellationToken);

		ok.ShouldBeFalse();
	}

	[Fact]
	public async Task SaveSessionModeAsync_DoesNotThrow_WhenWorkspacePathIsNull()
	{
		SessionModePersistence persistence = CreatePersistence();
		SessionModel session = new()
		{
			Id = "s5",
			Title = "Test",
			CreatedAt = DateTime.UtcNow,
			LastActivity = DateTime.UtcNow,
			Model = testModel,
			Context = new()
			{
				CurrentWorkingDirectory = string.Empty,
				WorkspacePath = null,
				GitRoot = null,
				Branch = null,
				Repository = null
			}
		};

		await Should.NotThrowAsync(() => persistence.SaveSessionMode(session, TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task TryRestoreSessionModeAsync_ReturnsFalse_WhenWorkspacePathIsNull()
	{
		SessionModePersistence persistence = CreatePersistence();
		SessionModel session = new()
		{
			Id = "s6",
			Title = "Test",
			CreatedAt = DateTime.UtcNow,
			LastActivity = DateTime.UtcNow,
			Model = testModel,
			Context = new()
			{
				CurrentWorkingDirectory = string.Empty,
				WorkspacePath = null,
				GitRoot = null,
				Branch = null,
				Repository = null
			}
		};

		bool ok = await persistence.TryRestoreSessionMode(session, TestContext.Current.CancellationToken);

		ok.ShouldBeFalse();
	}

	[Fact]
	public async Task SaveSessionModeAsync_OverwritesExistingFile()
	{
		SessionModePersistence persistence = CreatePersistence();
		SessionModel session = MakeSession("s7", SessionAgentModeEnum.Plan);
		await persistence.SaveSessionMode(session, TestContext.Current.CancellationToken);

		session.Context.SelectedAgentMode = SessionAgentModeEnum.Autopilot;
		await persistence.SaveSessionMode(session, TestContext.Current.CancellationToken);

		SessionModel restored = MakeSession("s7");
		bool ok = await persistence.TryRestoreSessionMode(restored, TestContext.Current.CancellationToken);

		ok.ShouldBeTrue();
		restored.Context.SelectedAgentMode.ShouldBe(SessionAgentModeEnum.Autopilot);
	}
}
