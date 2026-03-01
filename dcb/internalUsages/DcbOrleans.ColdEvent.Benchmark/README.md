# DcbOrleans.ColdEvent.Benchmark

BenchmarkDotNet project for comparing Cold Event storage backends (`jsonl`, `sqlite`, `duckdb`) in two paths:

- cold segment export (upsert)
- cold segment read + projection (JSONL parse and aggregation)

## Run

```bash
dotnet run -c Release --project /Users/tomohisa/dev/GitHub/Sekiban-dcb/dcb/internalUsages/DcbOrleans.ColdEvent.Benchmark/DcbOrleans.ColdEvent.Benchmark.csproj
```

## Notes

- Memory metrics are enabled via `MemoryDiagnoser`.
- Default parameter set:
  - `EventCount = 50000`
  - `PayloadSize = 96`
  - `StorageType = jsonl | sqlite | duckdb`
- Temporary benchmark files are created under system temp and removed on cleanup.
