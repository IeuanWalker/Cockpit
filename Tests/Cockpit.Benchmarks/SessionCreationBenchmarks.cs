using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.Emit;

// BenchmarkDotNet requires benchmark methods to be public instance members; CA1822 would
// otherwise suggest making these static.
#pragma warning disable CA1822

namespace Cockpit.Benchmarks;

/// <summary>
/// Benchmarks the orchestration of <c>SessionFeature.CreateSession</c>.
///
/// <para>
/// <c>CreateSession</c> is dominated by <em>awaited I/O</em> — spawning git subprocesses,
/// the SDK <c>CreateSessionAsync</c> IPC round-trip, the context-panel SDK reads, and three
/// best-effort disk writes. None of that work is CPU/allocation bound, so a classic
/// micro-benchmark of the synchronous object construction would measure the wrong thing.
/// </para>
///
/// <para>
/// What actually moves the wall-clock is the <em>ordering</em> of those awaits: whether
/// independent operations run sequentially or are overlapped. This benchmark models the two
/// orderings with representative I/O latencies (stand-ins for real operations — see the
/// constants below) so the effect of the optimization is reproducible. The absolute numbers
/// are modeled latencies, not measured production timings; the meaningful output is the
/// <c>Sequential</c> vs <c>Optimized</c> ratio.
/// </para>
/// </summary>
[MemoryDiagnoser]
[Config(typeof(Config))]
public class SessionCreationBenchmarks
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

	// Representative I/O latencies (milliseconds) for each awaited step in CreateSession.
	// Values are above the Windows timer-resolution floor (~15 ms) so Task.Delay models them
	// with reasonable accuracy. They approximate real-world magnitudes:

	// GetDefaultModel: cached after startup → effectively free in the common (warm) case.
	const int modelResolveMs = 0;

	// GetContext: spawns 3 git subprocesses concurrently → one process-spawn's worth of wall time.
	const int gitContextMs = 30;

	// client.CreateSessionAsync: a single SDK IPC round-trip.
	const int createSdkSessionMs = 30;

	// LoadContextPanelDataAsync: 5 concurrent SDK reads.
	const int loadContextPanelMs = 20;

	// Each of SaveSessionModel / SaveSessionAgent / SaveSessionMode: a small atomic file write.
	const int saveMs = 16;

	// SwitchCurrentSessionAsync: identical in both orderings (included for a realistic total).
	const int switchMs = 16;

	/// <summary>Models an awaited asynchronous I/O operation of the given duration.</summary>
	static Task Io(int milliseconds) => milliseconds <= 0 ? Task.CompletedTask : Task.Delay(milliseconds);

	/// <summary>
	/// Current ordering: every awaited step runs strictly one after another. Git context is
	/// resolved before the SDK round-trip, and the three saves are awaited individually.
	/// </summary>
	[Benchmark(Baseline = true)]
	public async Task Sequential()
	{
		await Io(modelResolveMs);        // GetDefaultModel (+ synchronous GetProviderConfig)
		await Io(gitContextMs);          // GetContext — blocks here before doing anything else
		// build SessionConfig (synchronous)
		await Io(createSdkSessionMs);    // client.CreateSessionAsync
		await Io(loadContextPanelMs);    // LoadContextPanelDataAsync
		await Io(saveMs);                // SaveSessionModel
		await Io(saveMs);                // SaveSessionAgent
		await Io(saveMs);                // SaveSessionMode
		await Io(switchMs);              // SwitchCurrentSessionAsync
	}

	/// <summary>
	/// Optimized ordering: git context is started up front so it overlaps both the model
	/// resolution and the SDK <c>CreateSessionAsync</c> round-trip (its result isn't needed
	/// until the SessionModel is built). The three persistence writes are best-effort resume
	/// metadata, so they're dispatched to the background and the user is never made to wait on
	/// them — hence they're absent from this measured (user-perceived) path.
	/// </summary>
	[Benchmark]
	public async Task Optimized()
	{
		Task<int> gitContextTask = ResolveGitContext();   // start early, don't await yet

		await Io(modelResolveMs);        // GetDefaultModel (+ synchronous GetProviderConfig)
		// build SessionConfig (synchronous) — does not need git context
		await Io(createSdkSessionMs);    // client.CreateSessionAsync — git runs concurrently

		_ = await gitContextTask;        // await git just before it's consumed (already done)

		await Io(loadContextPanelMs);    // LoadContextPanelDataAsync

		// Persistence (3 file writes) is fire-and-forget in production — the user doesn't wait
		// for it, so it isn't part of the perceived create latency modeled here.

		await Io(switchMs);              // SwitchCurrentSessionAsync
	}

	static async Task<int> ResolveGitContext()
	{
		await Io(gitContextMs);
		return 0;
	}
}
