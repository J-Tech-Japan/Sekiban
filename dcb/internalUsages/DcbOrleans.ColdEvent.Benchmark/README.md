# DcbOrleans.ColdEvent.Benchmark

BenchmarkDotNet project for comparing Cold Event storage backends (`jsonl`, `sqlite`, `duckdb`) in two paths:

- cold segment export (upsert)
- cold segment read + projection (JSONL parse and aggregation)
- snapshot write/read memory profile (10MB binary snapshot, 100MB JSON snapshot)
- snapshot build memory profile from event sources:
  - hot-only event store path
  - cold+hot merged path (hybrid catch-up style)

## Run

```bash
dotnet run -c Release --project ./DcbOrleans.ColdEvent.Benchmark.csproj
```

Run only snapshot memory benchmarks:

```bash
dotnet run -c Release --project ./DcbOrleans.ColdEvent.Benchmark.csproj -- --filter *SnapshotMemoryBenchmarks*
```

## Notes

- Memory metrics are enabled via `MemoryDiagnoser`.
- Default parameter set:
- `EventCount = 50000`
- `PayloadSize = 96`
- `BinarySnapshotSizeMb = 10`
- `JsonSnapshotSizeMb = 100`
  - `StorageType = jsonl | sqlite | duckdb`
- Temporary benchmark files are created under system temp and removed on cleanup.
