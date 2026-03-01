# Cold Event BenchmarkDotNet Results (2026-02-28)

## Environment
- Date: 2026-02-28 (America/Los_Angeles)
- Machine: Apple M4 Max
- Runtime: .NET 10.0.0
- BenchmarkDotNet: 0.15.6

## Command
```bash
dotnet run -c Release --project /Users/tomohisa/dev/GitHub/Sekiban-dcb/dcb/internalUsages/DcbOrleans.ColdEvent.Benchmark/DcbOrleans.ColdEvent.Benchmark.csproj
```

## Parameters
- EventCount: 50,000
- PayloadSize: 96
- StorageType: `sqlite`, `jsonl`, `duckdb`

## Summary Table
| Method | StorageType | Mean | Allocated |
|---|---:|---:|---:|
| Cold segment export (upsert) | sqlite | 1.989 ms | 1208 B |
| Cold segment export (upsert) | jsonl | 2.427 ms | 841 B |
| Cold segment read + projection | duckdb | 4.418 ms | 7208 B |
| Cold segment export (upsert) | duckdb | 10.664 ms | 3208 B |
| Cold segment read + projection | jsonl | 27.393 ms | 49,904,340 B |
| Cold segment read + projection | sqlite | 27.996 ms | 49,904,995 B |

## Quick Findings
- Read + projection fastest: `duckdb` (4.418 ms)
- Export fastest: `sqlite` (1.989 ms)
- Lowest allocated overall: `jsonl` export (841 B)
- Lowest allocated for read + projection: `duckdb` (7208 B)

## Raw Artifacts
- `/Users/tomohisa/dev/GitHub/Sekiban-dcb/BenchmarkDotNet.Artifacts/results/ColdEventProjectionBenchmarks-report-github.md`
- `/Users/tomohisa/dev/GitHub/Sekiban-dcb/BenchmarkDotNet.Artifacts/results/ColdEventProjectionBenchmarks-report.csv`
- `/Users/tomohisa/dev/GitHub/Sekiban-dcb/BenchmarkDotNet.Artifacts/results/ColdEventProjectionBenchmarks-report.html`
