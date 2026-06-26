using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using Cockpit.Features.Sessions;
using Cockpit.Features.Sessions.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelInfo = GitHub.Copilot.ModelInfo;
using SdkSessionContext = GitHub.Copilot.SessionContext;
using SdkSessionMetadata = GitHub.Copilot.SessionMetadata;

namespace Cockpit.Benchmarks;

/// <summary>
/// Benchmarks the in-memory work performed by <see cref="SessionFeature.RefreshExistingSessions"/>
/// when materializing SDK session metadata into <see cref="SessionModel"/> instances at startup.
/// The SDK/network round-trips are intentionally excluded — only the CPU/allocation cost of the
/// loop that was the subject of the optimization is measured.
/// </summary>
[MemoryDiagnoser]
[Config(typeof(Config))]
public class SessionLoadingBenchmarks
{
	sealed class Config : ManualConfig
	{
		public Config()
		{
			// In-process toolchain: BenchmarkDotNet runs the benchmark in this process instead of
			// generating and compiling a child project. That avoids re-triggering the Copilot CLI
			// download target on the referenced Cockpit project during benchmark execution.
			AddJob(Job.ShortRun.WithToolchain(InProcessEmitToolchain.Instance));
		}
	}

	[Params(50, 250, 1000)]
	public int SessionCount;

	List<SdkSessionMetadata> _metadata = [];
	ModelInfo _defaultModel = null!;

	[GlobalSetup]
	public void Setup()
	{
		_defaultModel = new ModelInfo
		{
			Id = "auto",
			Name = "Auto",
			DefaultReasoningEffort = "medium"
		};

		string validDir = AppContext.BaseDirectory;

		_metadata = new List<SdkSessionMetadata>(SessionCount);
		for(int i = 0; i < SessionCount; i++)
		{
			_metadata.Add(new SdkSessionMetadata
			{
				SessionId = $"session-{i:D6}-{Guid.NewGuid():N}",
				Summary = i % 3 == 0 ? null! : $"Session summary number {i}",
				StartTime = DateTimeOffset.UtcNow.AddMinutes(-i),
				ModifiedTime = DateTimeOffset.UtcNow.AddMinutes(-(i / 2.0)),
				Context = new SdkSessionContext
				{
					// Mix of resolvable paths and null to exercise both normalization branches.
					WorkingDirectory = i % 2 == 0 ? validDir : null,
					GitRoot = i % 2 == 0 ? validDir : null,
					Repository = i % 2 == 0 ? "owner/repo" : null,
					Branch = i % 2 == 0 ? "main" : null
				}
			});
		}
	}

	/// <summary>
	/// The original loop: builds a HashSet, then for each metadata creates a SessionModel,
	/// calls <c>ApplyContextConsistency</c> (a second, redundant normalization) and inserts each
	/// session one-by-one at the front of the list via <c>AddSession</c> (O(n) per insert).
	/// </summary>
	[Benchmark(Baseline = true)]
	public SessionListFeature Original()
	{
		SessionListFeature feature = new(NullLogger<SessionListFeature>.Instance);

		HashSet<string> existingSessionIds = new(feature.Sessions.Select(s => s.Id), StringComparer.Ordinal);

		foreach(SdkSessionMetadata metadata in _metadata)
		{
			if(existingSessionIds.Contains(metadata.SessionId))
			{
				continue;
			}

			try
			{
				string? cwd = SessionWorkingDirectoryNormalizer.Normalize(metadata.Context?.WorkingDirectory);

				SessionModel chatSession = new()
				{
					Id = metadata.SessionId,
					Title = metadata.Summary ?? $"Session {metadata.SessionId[..8]}",
					CreatedAt = metadata.StartTime.UtcDateTime,
					LastActivity = metadata.ModifiedTime.UtcDateTime,
					Status = SessionStatusEnum.Idle,
					Model = _defaultModel,
					ReasoningEffort = _defaultModel.DefaultReasoningEffort,
					Context = new()
					{
						CurrentWorkingDirectory = cwd,
						WorkspacePath = null,
						GitRoot = cwd is null ? null : metadata.Context?.GitRoot,
						Repository = cwd is null ? null : metadata.Context?.Repository,
						Branch = cwd is null ? null : metadata.Context?.Branch
					}
				};

				SessionWorkingDirectoryNormalizer.ApplyContextConsistency(chatSession.Context);

				feature.AddSession(chatSession);
			}
			catch
			{
				// Mirror the original swallow-and-continue behaviour.
			}
		}

		return feature;
	}

	/// <summary>
	/// The optimized production path: single normalization per session and one O(n) batch insert.
	/// </summary>
	[Benchmark]
	public SessionListFeature Optimized()
	{
		SessionListFeature feature = new(NullLogger<SessionListFeature>.Instance);
		SessionFeature.PopulateSessionsFromMetadata(_metadata, _defaultModel, feature, NullLogger.Instance);
		return feature;
	}
}
