# Snapshot Memory BenchmarkDotNet Results (2026-03-01)

## Context

- Source intent doc: `concepts/products/sekiban/memory-optimization-and-benchmarking.md`
- Goal: quantify memory/GC/runtime for snapshot read/write paths, especially around:
  - 10MB snapshot payload (binary)
  - 100MB snapshot JSON read/write path ("10MB snapshot may correspond to ~100MB JSON" scenario)

## Environment

- Date: 2026-03-01
- BenchmarkDotNet: 0.15.6
- Runtime: .NET 10.0.0
- Machine: Apple M4 Max (Arm64), macOS 26.3

## Command

```bash
dotnet run -c Release --project dcb/internalUsages/DcbOrleans.ColdEvent.Benchmark/DcbOrleans.ColdEvent.Benchmark.csproj -- --filter "*SnapshotMemoryBenchmarks*" --job short --warmupCount 1 --iterationCount 3
```

## Key Results (from `BenchmarkDotNet.Artifacts/results/SnapshotMemoryBenchmarks-report-github.md`)

| Method | StorageType | Mean | Allocated |
|---|---|---:|---:|
| Snapshot read (10MB binary) | jsonl | 819.0 us | 10,486,810 B |
| Snapshot read (10MB binary) | sqlite | 1,957.8 us | 10,487,027 B |
| Snapshot read (10MB binary) | duckdb | 7,584.4 us | 43,913,387 B |
| Snapshot write (10MB binary) | jsonl | 2,078.6 us | 865 B |
| Snapshot write (10MB binary) | sqlite | 1,726.4 us | 1,208 B |
| Snapshot write (10MB binary) | duckdb | 44,627.2 us | 3,232 B |
| Snapshot read + parse (100MB JSON) | jsonl | 50,222.1 us | 104,858,310 B |
| Snapshot read + parse (100MB JSON) | sqlite | 59,195.7 us | 104,859,035 B |
| Snapshot read + parse (100MB JSON) | duckdb | 89,705.8 us | 373,166,988 B |
| Snapshot write (100MB JSON) | jsonl | 18,322.3 us | 871 B |
| Snapshot write (100MB JSON) | sqlite | 20,878.9 us | 1,208 B |
| Snapshot write (100MB JSON) | duckdb | 66,837.2 us | 3,232 B |

## Notes

- Read-path allocations for 100MB JSON are roughly ~100MB for `jsonl`/`sqlite` and significantly larger for `duckdb` in this benchmark implementation.
- During this task, `DuckDbColdBenchmarkStorage.Get` was updated to handle both `byte[]` and `Stream` return shapes for large BLOB reads.

## Artifacts

- `BenchmarkDotNet.Artifacts/results/SnapshotMemoryBenchmarks-report-github.md`
- `BenchmarkDotNet.Artifacts/results/SnapshotMemoryBenchmarks-report.csv`
- `BenchmarkDotNet.Artifacts/results/SnapshotMemoryBenchmarks-report.html`

## Additional Check: Snapshot Build Memory (Hot vs Cold+Hot)

### Command

```bash
dotnet run -c Release --project dcb/internalUsages/DcbOrleans.ColdEvent.Benchmark/DcbOrleans.ColdEvent.Benchmark.csproj -- --filter "*SnapshotBuildMemoryBenchmarks*" --job short --warmupCount 1 --iterationCount 3
```

### Summary (EventCount=60,000, PayloadSize=120)

| Method | StorageType | Mean | Allocated |
|---|---|---:|---:|
| Snapshot build from hot events only | jsonl | 12.67 ms | 25.25 MB |
| Snapshot build from cold+hot merged events | jsonl | 50.08 ms | 83.19 MB |
| Snapshot build from hot events only | sqlite | 13.03 ms | 25.25 MB |
| Snapshot build from cold+hot merged events | sqlite | 51.12 ms | 83.19 MB |
| Snapshot build from hot events only | duckdb | 17.37 ms | 41.12 MB |
| Snapshot build from cold+hot merged events | duckdb | 58.30 ms | 130.95 MB |

### Interpretation

- Snapshot creation itself is shared logic, but cold+hot merge path allocates more due to merge/dedup/sort.
- This benchmark now explicitly validates memory impact for both event source paths before snapshot generation.

### Additional Artifacts

- `BenchmarkDotNet.Artifacts/results/SnapshotBuildMemoryBenchmarks-report-github.md`
- `BenchmarkDotNet.Artifacts/results/SnapshotBuildMemoryBenchmarks-report.csv`
- `BenchmarkDotNet.Artifacts/results/SnapshotBuildMemoryBenchmarks-report.html`
