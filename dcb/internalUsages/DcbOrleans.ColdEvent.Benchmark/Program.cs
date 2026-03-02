using BenchmarkDotNet.Running;

BenchmarkSwitcher
    .FromTypes([
        typeof(ColdEventProjectionBenchmarks),
        typeof(SnapshotMemoryBenchmarks),
        typeof(SnapshotBuildMemoryBenchmarks)
    ])
    .Run(args);
