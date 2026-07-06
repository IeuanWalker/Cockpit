using Cockpit.Features.Sessions;
using Cockpit.Features.Sessions.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using ModelInfo = GitHub.Copilot.ModelInfo;
using SdkSessionContext = GitHub.Copilot.SessionContext;
using SdkSessionMetadata = GitHub.Copilot.SessionMetadata;

namespace Cockpit.UnitTests.Features.Sessions;

/// <summary>
/// Locks in the behaviour of <see cref="SessionFeature.PopulateSessionsFromMetadata"/>, the
/// extracted/optimized startup loop that materializes SDK session metadata into
/// <see cref="SessionModel"/> instances.
/// </summary>
public sealed class PopulateSessionsFromMetadataTests : IDisposable
{
	readonly string _realDir;
	readonly ModelInfo _model = new()
	{
		Id = "auto",
		Name = "Auto",
		DefaultReasoningEffort = "medium"
	};

	public PopulateSessionsFromMetadataTests()
	{
		_realDir = Path.Combine(Path.GetTempPath(), "CockpitPopulateTests_" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_realDir);
	}

	public void Dispose()
	{
		if(Directory.Exists(_realDir))
		{
			Directory.Delete(_realDir, recursive: true);
		}
	}

	static SessionListFeature NewFeature() => new(NullLogger<SessionListFeature>.Instance);

	static void Populate(SessionListFeature feature, IList<SdkSessionMetadata> metadata, ModelInfo model)
		=> SessionFeature.PopulateSessionsFromMetadata(metadata, model, feature, NullLogger.Instance);

	SdkSessionMetadata MakeMetadata(string id, string? summary, bool withWorkingDir)
		=> new()
		{
			SessionId = id,
			Summary = summary!,
			StartTime = new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero),
			ModifiedTime = new DateTimeOffset(2024, 1, 2, 6, 7, 8, TimeSpan.Zero),
			Context = new SdkSessionContext
			{
				WorkingDirectory = withWorkingDir ? _realDir : null!,
				GitRoot = withWorkingDir ? _realDir : null!,
				Repository = withWorkingDir ? "owner/repo" : null!,
				Branch = withWorkingDir ? "main" : null!
			}
		};

	[Fact]
	public void MapsAllFields_ForSessionWithWorkingDirectory()
	{
		SessionListFeature feature = NewFeature();
		SdkSessionMetadata metadata = MakeMetadata("session-aaaaaaaa-1", "My summary", withWorkingDir: true);

		Populate(feature, [metadata], _model);

		SessionModel session = feature.Sessions.ShouldHaveSingleItem();
		session.Id.ShouldBe("session-aaaaaaaa-1");
		session.Title.ShouldBe("My summary");
		session.Status.ShouldBe(SessionStatusEnum.Idle);
		session.Model.ShouldBeSameAs(_model);
		session.ReasoningEffort.ShouldBe("medium");
		session.CreatedAt.ShouldBe(metadata.StartTime.UtcDateTime);
		session.LastActivity.ShouldBe(metadata.ModifiedTime.UtcDateTime);
		session.Context.CurrentWorkingDirectory.ShouldBe(Path.GetFullPath(_realDir));
		session.Context.WorkspacePath.ShouldBeNull();
		session.Context.GitRoot.ShouldBe(_realDir);
		session.Context.Repository.ShouldBe("owner/repo");
		session.Context.Branch.ShouldBe("main");
	}

	[Fact]
	public void NullsGitFields_WhenWorkingDirectoryMissing()
	{
		SessionListFeature feature = NewFeature();
		SdkSessionMetadata metadata = MakeMetadata("session-bbbbbbbb-2", "Summary", withWorkingDir: false);

		Populate(feature, [metadata], _model);

		SessionModel session = feature.Sessions.ShouldHaveSingleItem();
		session.Context.CurrentWorkingDirectory.ShouldBeNull();
		session.Context.GitRoot.ShouldBeNull();
		session.Context.Repository.ShouldBeNull();
		session.Context.Branch.ShouldBeNull();
	}

	[Fact]
	public void FallsBackTo_TruncatedIdTitle_WhenSummaryNull()
	{
		SessionListFeature feature = NewFeature();
		SdkSessionMetadata metadata = MakeMetadata("abcdefgh-rest-of-id", summary: null, withWorkingDir: false);

		Populate(feature, [metadata], _model);

		feature.Sessions.ShouldHaveSingleItem().Title.ShouldBe("Session abcdefgh");
	}

	[Fact]
	public void PreservesOrdering_NewestProcessedFirst_LikeInsertAtFront()
	{
		SessionListFeature feature = NewFeature();
		IList<SdkSessionMetadata> metadata =
		[
			MakeMetadata("id-0", "s0", false),
			MakeMetadata("id-1", "s1", false),
			MakeMetadata("id-2", "s2", false)
		];

		Populate(feature, metadata, _model);

		// Equivalent to calling AddSession (Insert at index 0) for each in order: last ends up first.
		feature.Sessions.Select(s => s.Id).ShouldBe(["id-2", "id-1", "id-0"]);
	}

	[Fact]
	public void SkipsSessions_AlreadyPresentById()
	{
		SessionListFeature feature = NewFeature();
		Populate(feature, [MakeMetadata("dup", "first", false)], _model);

		// Second populate with the same id plus a new one.
		Populate(feature, [MakeMetadata("dup", "second", false), MakeMetadata("new", "fresh", false)], _model);

		feature.Sessions.Count.ShouldBe(2);
		feature.Sessions.Count(s => s.Id == "dup").ShouldBe(1);
		// The original "dup" (Title "first") is retained; the duplicate is skipped.
		feature.Sessions.Single(s => s.Id == "dup").Title.ShouldBe("first");
		feature.Sessions.ShouldContain(s => s.Id == "new");
	}

	[Fact]
	public void SkipsDuplicates_WithinSameBatch()
	{
		SessionListFeature feature = NewFeature();

		Populate(feature, [MakeMetadata("x", "a", false), MakeMetadata("x", "b", false)], _model);

		feature.Sessions.ShouldHaveSingleItem().Title.ShouldBe("a");
	}
}
