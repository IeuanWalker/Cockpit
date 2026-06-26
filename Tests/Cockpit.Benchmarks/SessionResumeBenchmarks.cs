using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.Emit;

// BenchmarkDotNet requires benchmark methods to be public instance members; CA1822 would
// otherwise suggest making these static.
#pragma warning disable CA1822

namespace Cockpit.Benchmarks;

/// <summary>
/// Benchmarks the orchestration of <c>SessionFeature.LoadSession</c> (the user-facing resume path).
///
/// <para>
/// Resuming replays every event so the UI is reconstructed exactly as it was left — that cost grows
/// with session length and is intentional. What the optimization changes is the <em>ordering</em> of
/// the surrounding awaits: the context-panel SDK reads (agents / instructions / MCP / skills / plugins)
/// are independent of the replay (the replay never touches <c>session.Context</c>), so they can run
/// concurrently with the event fetch + replay instead of before it.
/// </para>
///
/// <para>
/// The benchmark models both orderings with representative I/O latencies, parameterized by the replay
/// duration (a proxy for session length). The takeaway it makes visible: the overlap hides up to the
/// fixed context-panel cost, which is a meaningful fraction of a short session's resume but negligible
/// next to a long replay — i.e. it helps most where the replay is cheap. Absolute numbers are modeled
/// latencies, not measured production timings; the <c>Sequential</c> vs <c>Optimized</c> ratio is the
/// meaningful output.
/// </para>
/// </summary>
[MemoryDiagnoser]
[Config(typeof(Config))]
public class SessionResumeBenchmarks
{
	sealed class Config : ManualConfig
	{
		public Config()
		{
			// In-process toolchain so BenchmarkDotNet doesn't generate/compile a child project
			// (which would re-trigger the Copilot CLI download target on the referenced app).
			AddJob(Job.ShortRun.WithToolchain(InProcessEmitToolchain.Instance));
		}
	}

	// Modeled I/O latencies (milliseconds), above the Windows timer-resolution floor (~15 ms):

	// client.ResumeSessionAsync: one SDK IPC round-trip (the SDK loads the session here). Identical
	// in both orderings — included for a realistic total.
	const int resumeIpcMs = 32;

	// LoadContextPanelDataAsync: 5 concurrent SDK reads.
	const int contextPanelMs = 20;

	// sdkSession.GetEventsAsync: one IPC that returns the event list.
	const int getEventsMs = 16;

	// Restore tail (model/agent/mode reads + the agent/mode RPCs). Identical in both orderings.
	const int restoreMs = 16;

	/// <summary>
	/// Replay duration — a proxy for session length. 0 ≈ a brand-new/short session; the larger
	/// values represent progressively longer histories where the replay dominates.
	/// </summary>
	[Params(0, 64, 512)]
	public int ReplayMs;

	static Task Io(int milliseconds) => milliseconds <= 0 ? Task.CompletedTask : Task.Delay(milliseconds);

	/// <summary>
	/// Current ordering: the context-panel load is awaited up front, before the event fetch + replay.
	/// </summary>
	[Benchmark(Baseline = true)]
	public async Task Sequential()
	{
		await Io(resumeIpcMs);       // client.ResumeSessionAsync
		await Io(contextPanelMs);    // LoadContextPanelDataAsync — blocks here before the replay
		await Io(getEventsMs);       // sdkSession.GetEventsAsync
		await Io(ReplayMs);          // Task.Run replay of all events
		await Io(restoreMs);         // restore model/agent/mode + SDK Select/Set
	}

	/// <summary>
	/// Optimized ordering: the context-panel load is started right after the SDK session is obtained
	/// and runs concurrently with the event fetch + replay, then joined just before the restore section
	/// (which reads session.Context).
	/// </summary>
	[Benchmark]
	public async Task Optimized()
	{
		await Io(resumeIpcMs);                       // client.ResumeSessionAsync

		Task contextPanelTask = Io(contextPanelMs);  // start, don't await yet

		await Io(getEventsMs);                       // sdkSession.GetEventsAsync — overlaps panel
		await Io(ReplayMs);                          // Task.Run replay — overlaps panel

		await contextPanelTask;                      // join before reading session.Context

		await Io(restoreMs);                         // restore model/agent/mode + SDK Select/Set
	}
}
