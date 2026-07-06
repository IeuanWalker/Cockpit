using BenchmarkDotNet.Running;
using Cockpit.Benchmarks;

// Run a specific benchmark via args (e.g. --filter *SessionCreation*), otherwise run all.
BenchmarkSwitcher
	.FromTypes([typeof(SessionLoadingBenchmarks), typeof(SessionCreationBenchmarks), typeof(SessionResumeBenchmarks)])
	.Run(args);
