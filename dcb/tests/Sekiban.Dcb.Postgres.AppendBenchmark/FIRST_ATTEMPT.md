# SEK-G162 local verification — acceptance failed

Measured 2026-11-13 against `origin/main` / `e1478efefc578db03e68b06d66a1898270530db0`. Only `dcb/src/Sekiban.Dcb.Postgres/PostgresEventStore.cs` was temporarily replaced for main; the benchmark and new count test were identical. The branch file was restored from a byte-for-byte copy, `cmp` passed, and `git diff` again showed the seam changes. No commit, push, tag, PR or publication was made.

The performance gates fail. Stop here for conductor review; no benchmark tuning, discarded runs or reruns were used. All six cases below are the one prescribed warm-up plus five measured runs, five seconds per run, for each implementation. Other builds/tests completed before timing. Variation is retained in the raw results.

## Machine and method

Apple M4 Max MacBook Pro (`Mac16,5`), 16 CPU cores, 128 GB RAM, macOS 27.2.1 (26B131), arm64. .NET SDK 11.0.100, benchmark runtime 10.0.12. Docker Desktop Linux aarch64 engine, 16 CPUs, PostgreSQL `postgres:16-alpine`. Both tests' target frameworks are net9.0 and net10.0. The benchmark runs on net10.0.

Writer: ordinary typed `WriteEventsAsync` → `TagHeadWriter.TypedBatch`, no expectations. Fixed heads are seeded before timing. All measured appends succeeded. Throughput is the median of five measured runs; p50/p99 are the medians of per-run latency percentiles in milliseconds. Successful counts below sum all five measured runs, excluding seeds and warm-up. [Method and runnable commands](README.md); every individual result and successful count: main (local evidence, not committed), branch (local evidence, not committed).

## Benchmark

| Case | Main appends/s | Branch appends/s | Branch/main | Gate | Main p50/p99 ms | Branch p50/p99 ms | Successful writes main/branch |
|---|---:|---:|---:|---|---:|---:|---:|
| 1 tag | 477.77 | 374.63 | 78.4% | FAIL (≥95%) | 2.045/2.922 | 2.562/4.206 | 11890/9114 |
| 5 tags | 189.20 | 206.23 | 109.0% | FAIL (≥110%) | 5.198/7.151 | 3.167/55.988 | 4740/5191 |
| 20 tags | 58.14 | 150.53 | 258.9% | PASS (≥110%) | 16.913/21.431 | 4.860/48.348 | 1454/3687 |
| shared only | 569.40 | 489.85 | 86.0% | FAIL (≥95%) | 27.541/38.714 | 32.224/45.220 | 14343/12388 |
| shared+4 | 276.12 | 443.40 | 160.6% | PASS (≥110%) | 57.290/73.428 | 35.273/50.627 | 6866/11027 |
| disjoint 5 | 1198.61 | 2484.25 | 207.3% | reported | 12.854/24.485 | 5.910/14.015 | 30069/61353 |

## Npgsql statement counts

Counts match on net9.0 and net10.0. The successful case reuses existing heads. Repair-only conflict repairs every existing head and cleans up an equally sized set of unrelated newly created heads. Each comparison therefore has the same subset shape at 1, 5 and 20 tags. Empty subsets are skipped in production. Npgsql activities are sampled with all data and filtered by the test parent TraceId; the recorder supports `db.statement` and `db.query.text`. EF event/tag DML and epoch reads are excluded. Counts are asserted nonzero.

| Scenario | Implementation | 1 tag | 5 tags | 20 tags |
|---|---|---:|---:|---:|
| Success | main | 4 | 20 | 80 |
| Success | branch | 4 | 4 | 4 |
| Repair-only conflict | main | 10 | 50 | 200 |
| Repair-only conflict | branch | 7 | 7 | 7 |

On main, the new constant-count test intentionally fails once per target framework after printing all six counts (`Expected: 4; Actual: 20`). All existing tests were left unchanged.

## SQL by phase

All raw commands use the seam's existing connection and transaction command helpers. Arrays carry the existing C# `StringComparer.Ordinal` ordering, by service ID then tag. There is no SQL collation-based key ordering. All six protocol hooks retain their original boundaries, including Phase 1 entry before the first insert and no hook between insert and lock.

1. Lazy heads: one `INSERT INTO dcb_tag_heads (...) SELECT k.service_id, k.tag, NULL FROM unnest(@serviceIds, @tags) WITH ORDINALITY AS k(service_id, tag, ord) ORDER BY k.ord ON CONFLICT (...) DO NOTHING RETURNING ...`.
2. Lock: one `SELECT h... FROM unnest(...) WITH ORDINALITY AS k(...) JOIN dcb_tag_heads h ON ... ORDER BY k.ord FOR UPDATE OF h`. Newly created keys only: one `UPDATE dcb_tag_heads ... FROM (SELECT ... MAX(t."SortableUniqueId") ... LEFT JOIN dcb_tags ... GROUP BY ...) ... RETURNING ...`; skip when none were created.
3. Reconcile, as a separate statement after every lock wait: `SELECT k..., m.position FROM unnest(@serviceIds, @tags, @positions) WITH ORDINALITY ... CROSS JOIN LATERAL (...) m WHERE m.position IS NOT NULL ORDER BY k.ord`. The lateral union contains the authoritative `MAX` for `SortableUniqueId > k.position` and the authoritative `MAX` for `k.position IS NULL`, preserving indexable ranges. Repaired keys only: one ordered `INSERT ... SELECT FROM unnest(...) WITH ORDINALITY ... ON CONFLICT DO NOTHING` for violations, then one `UPDATE ... FROM unnest(...)` for heads. Skip both if no repair exists. Returned repairs stay in canonical order.
4. Expectations still compare in C#. Ordinary mismatch still rolls back. Repair-only mismatch still commits only repairs/audit; lazy non-repaired keys are removed by one `DELETE FROM dcb_tag_heads USING unnest(...) WITH ORDINALITY ...`, skipped for an empty cleanup set.
5. EF event/tag insertion and its hooks are unchanged. C# still chooses `max(reconciled head, batch maximum)` with `StringComparer.Ordinal`. Advancing keys only: one `UPDATE dcb_tag_heads ... FROM unnest(@serviceIds, @tags, @positions) WITH ORDINALITY ...`; skip if none advance.

## Tests and commands

New tests in `PostgresSetBasedAppendTests`:

- `ManyTags_LazyExistingAndRepaired_PreserveHeadsAndAudit` (24 tags).
- `StatementCounts_AreConstant_ForSameShapeSuccessAndRepairOnlyConflict`.
- `ReversedUnicodeBatches_DoNotDeadlock` (mixed case, non-ASCII, supplementary-plane and U+E000 keys; ordinal versus linguistic/UTF-8 comparisons).
- `OrdinalOrder_DirectBlockingProbe` (four theory cases: lock and lazy insert, blocked ordinal-first and ordinal-second). An admin holds the target key, `pg_stat_activity` proves the seam is waiting, and a third connection checks prior ownership with `FOR UPDATE NOWAIT` or a bounded conflicting insert.

Final branch runs include all existing `PostgresTagHeadCasTests`, conditional append, repair/audit, fault injection, reversed batches, savepoints and seam architecture tests, unchanged.

| Command (from repo root) | net9.0 | net10.0 |
|---|---|---|
| `dotnet test dcb/tests/Sekiban.Dcb.Postgres.Tests -c Release -p:GenerateSBOM=false --no-restore --logger 'console;verbosity=detailed'` | 124 passed, 0 failed/skipped | 124 passed, 0 failed/skipped |
| `dotnet test dcb/tests/Sekiban.Dcb.WithResult.Tests -c Release -p:GenerateSBOM=false --no-build --no-restore --filter 'FullyQualifiedName~Postgres\|FullyQualifiedName~ConditionalAppend'` | 141 passed, 0 failed/skipped | 141 passed, 0 failed/skipped |
| `dotnet test dcb/tests/Sekiban.Dcb.Orleans.Tests -c Release -p:GenerateSBOM=false --no-build --no-restore --filter FullyQualifiedName~TwoClusterPostgresProductTests` | 12 passed, 0 failed/skipped | 12 passed, 0 failed/skipped |
| Same Postgres test command against main with `--no-build --filter FullyQualifiedName~StatementCounts_AreConstant` | 1 expected failure | 1 expected failure |

Both full rebuilds: `dotnet build dcb/Sekiban.Dcb.slnx -c Release -t:Rebuild -p:GenerateSBOM=false --no-restore` — pass, 365 warnings, 0 errors each. Warning-code sets match:

`CA1031 CA1032 CA1062 CA1305 CA1513 CA2007 CA2016 CS0184 CS0436 CS0618 CS0649 CS1591 CS8601 CS8602 CS8604 CS8767 IL2026 IL3050 NU1900 ORLEANS0014 xUnit1031 xUnit2013 xUnit2031`.

`NU1900` is the environment's denied NuGet vulnerability-cache write. Intermediate development invocations encountered constructor/SQL-edit compilation errors, corrected before the final passing runs above.

Benchmark builds use `-p:GenerateSBOM=false`. Timed commands:

```sh
dotnet run --project dcb/tests/Sekiban.Dcb.Postgres.AppendBenchmark -c Release -f net10.0 --no-build -- branch 5
dotnet run --project dcb/tests/Sekiban.Dcb.Postgres.AppendBenchmark -c Release -f net10.0 --no-build -- main 5
```

Both commands completed successfully: 36 runs each (6 warm-ups and 30 measured), no failed writes. See README for the exact source-copy/main/restore sequence. Restored branch rebuilt afterward. `git diff --check` passes. CI was not triggered because nothing was pushed or published; AC4 fails and AC5 remains for the conductor.
