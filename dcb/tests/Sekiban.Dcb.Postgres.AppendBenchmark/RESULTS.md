# SEK-G162 packet rev 5 — local verification

Approved design from issue #1412. Existing tests (including every rev-4 test) remain unchanged. Added `PostgresSetBasedProtocolTests.cs` and embedded main SQL fixtures. The only rev-5 product edit makes `serviceIds` and `tags` explicitly `NpgsqlDbType.Array | NpgsqlDbType.Text`; nullable `positions` arrays were already explicit. No product bug was exposed and no protocol/design change was made. These were local results before the PR; CI results are on PR #1413.

## New test mapping

All methods below are in `PostgresSetBasedProtocolTests` (20 cases on each TFM).

| Requirement | Test | Evidence |
|---|---|---|
| (a) | `MultiKey_InjectedStageFailure_RollsBackLazyRepairAuditAndWrites` | Hooks 3/4/5/6: after reconciliation/event DML/tag DML/head update; 2 repaired heads + 1 lazy head; independent Npgsql connection sees original heads/history only, no events/audit/lazy head, no warning. |
| (b) | `MultiKey_AuditRollbackThenRetry_IsIdempotentAndLogsOnlyCommittedRepairs` | Audit rollback is silent; retry commits exactly one audit per key and warning per repair; stale retry has no new audit/warning. Replay the exact recorded set-based INSERT twice against durable identities: both affect zero rows. |
| (c) | `MultiKey_AssertEmptyOrExactRace_CreatorCommitOrRollback_DetectsCreatedRows` | AssertEmpty/Exact × creator commits/rolls back. Both writers enter before insertion; observe second blocked on PostgreSQL. Exactly one append commits; both heads retained with winner's maximum; no spurious bootstrap repair/audit. Exact begins with history but absent heads, exercising bootstrap after creator rollback. |
| (d) | `MultiKey_EnforcedLegacyRace_UsesMaximumOfReconciledHeadAndBatch` | Both orders; repair 0100→0500; legacy batch 0300 cannot regress repair; Exact(0500) writer advances to 0600; both commit, exactly two audits. |
| mixed | `MixedPaths_PhaseOneEntryBarrier_BothOrdersCommitWithoutDeadlock` | Both writers synchronize at hook 1 before INSERT; first then second in both single/multi orders, observe real lock wait. Both commit, no 40P01, shared head max=0300 despite second batch=0200. |
| zero | `ZeroDistinctKeys_RecorderSeesNoHeadStatement` | Successful untagged append, zero recorded head statements. Rev-4 six-hook zero test also unchanged. |
| 1-key-text | `OneKey_RecordedTextsAndParameterNames_EqualMain` | Complete SQL text sequence and named parameters equal swapped-main fixtures for lazy/bootstrap/existing/repair, not merely counts. |
| empty subsets | `MultiKey_EmptySubsets_SkipBootstrapRepairCleanupAndAdvance` | Ordinary conflict and successful older legacy batch both send only insert/lock/reconcile (3); no bootstrap/repair/audit/cleanup/advance SQL. |

The unchanged rev-4 `StatementCounts_AreConstant_ForSameShapeSuccessAndRepairOnlyConflict` uses same-shape success, repair-only and repair+cleanup scenarios; 2/5/20-key counts remain constant (4/5/7). The real one-key success/repair-only counts remain 4/5, matching main. A one-row subset in a multi-key batch stays on set-based SQL. Subset statements are skipped when empty.

## Final correctness gates

Commands: `python3 dcb/tests/Sekiban.Dcb.Postgres.AppendBenchmark/run_correctness.py`. Release solution build and all three required suites on both TFMs, no failures/skips. Detailed logs in validation/rev5 (local evidence, not committed).

| Suite | net9.0 | net10.0 |
|---|---:|---:|
| Full Postgres.Tests | 150 passed | 150 passed |
| WithResult: Postgres or ConditionalAppend | 141 passed | 141 passed |
| Orleans TwoClusterPostgresProductTests | 12 passed | 12 passed |

AC1–AC3: local PASS (SQL parity, counts/order/scope, all correctness). AC4: PASS in each existing alternating block; new main-vs-main control quantifies noise below. AC5: verified by CI on PR #1413. `git diff --check`: PASS. Original source boundaries, hooks, epoch checks, public API, schema, dependencies, other stores and EF event/tag DML remain unchanged. Aspire MCP attachment was rejected by the session's `never` approval policy; tests use their existing disposable Testcontainers fixtures.

## Main source and benchmark method

Main is **origin/main at e1478efefc578db03e68b06d66a1898270530db0 (e1478efe)**, obtained with `git show <commit>:dcb/src/Sekiban.Dcb.Postgres/PostgresEventStore.cs`. Only that file is temporarily swapped. A temporary-file backup is restored **byte-for-byte in finally**, including failure paths. See main SQL capture evidence (local evidence, not committed), control restoration evidence (local evidence, not committed), [capture script](capture_main_sql.py), [control script](run_control.py), and [README](README.md).

The rev-4 main-1→branch-1→main-2→branch-2 raw results are preserved, not rerun or replaced. Added one main-A→main-B control block, back to back with the same unchanged `Program.cs`, machine, net10.0, per-case fresh tables, seeding outside timing, one warm-up and five measured five-second runs, successful appends only. Each invocation uses its own PostgreSQL 16 container, as in rev 4. No concurrent builds/tests/profilers during timing. Machine: Apple M4 Max, macOS 27.2.1, runtime .NET 10.0.12. All measured appends succeeded. No benchmark tuning.

## Full benchmark table

Throughput = median successful appends/s. Latencies = median of five per-run p50/p99 (ms). Writes = sum of five measured runs, excluding warm-up/seeding. Control columns are main-A/main-B instead of main/branch. Gates ≥95% for 1 tag/shared only, ≥110% for 5/20 tags/shared+4; disjoint reported only. A gate must pass in **both** alternating blocks.

| Case | Block | Main throughput | Branch throughput | Ratio | Gate | Main p50/p99 | Branch p50/p99 | Writes main/branch |
|---|---|---:|---:|---:|---|---:|---:|---:|
| 1 tag | 1 | 552.30 | 557.44 | 100.9% | PASS | 1.773/2.397 | 1.758/2.357 | 13781/13965 |
| 1 tag | 2 | 553.92 | 541.22 | 97.7% | PASS | 1.759/2.412 | 1.816/2.369 | 13856/13530 |
| 1 tag | control | 554.66 | 556.72 | 100.4% | control | 1.782/2.449 | 1.777/2.385 | 13843/13950 |
| 5 tags | 1 | 213.33 | 440.64 | 206.6% | PASS | 4.585/5.689 | 2.200/2.954 | 5305/11110 |
| 5 tags | 2 | 215.94 | 406.73 | 188.3% | PASS | 4.532/5.605 | 2.401/3.176 | 5390/10349 |
| 5 tags | control | 160.15 | 212.86 | 132.9% | control | 6.128/8.026 | 4.674/5.852 | 4315/5222 |
| 20 tags | 1 | 63.51 | 149.33 | 235.1% | PASS | 15.546/18.134 | 4.457/43.555 | 1595/3768 |
| 20 tags | 2 | 64.89 | 237.16 | 365.5% | PASS | 15.178/17.756 | 3.995/9.799 | 1619/5605 |
| 20 tags | control | 64.85 | 64.09 | 98.8% | control | 15.393/18.154 | 15.509/18.366 | 1595/1605 |
| shared only | 1 | 632.12 | 637.57 | 100.9% | PASS | 24.865/30.799 | 24.863/32.920 | 15872/15930 |
| shared only | 2 | 640.25 | 632.57 | 98.8% | PASS | 24.734/29.068 | 25.141/29.553 | 16093/15826 |
| shared only | control | 652.62 | 619.54 | 94.9% | control | 24.259/29.568 | 25.655/32.812 | 16344/15503 |
| shared+4 | 1 | 306.94 | 547.11 | 178.2% | PASS | 51.747/62.840 | 28.708/39.092 | 7718/13769 |
| shared+4 | 2 | 305.67 | 548.75 | 179.5% | PASS | 51.825/59.205 | 28.844/34.431 | 7675/13753 |
| shared+4 | control | 302.13 | 297.92 | 98.6% | control | 52.691/59.071 | 53.459/62.928 | 7635/7532 |
| disjoint 5 | 1 | 1318.41 | 2892.96 | 219.4% | reported | 11.705/22.890 | 5.018/15.764 | 33034/72218 |
| disjoint 5 | 2 | 1355.77 | 2882.85 | 212.6% | reported | 11.446/21.875 | 5.126/11.268 | 34046/72198 |
| disjoint 5 | control | 1054.83 | 1105.01 | 104.8% | control | 14.641/27.216 | 13.938/26.569 | 26526/28368 |

Control spread is `(main-B median / main-A median − 1) × 100%` (signed); absolute spread is its magnitude. This is a noise diagnostic, not another branch gate. Noise is material: five-tag main varies by +32.91%, and shared-only main varies by −5.07% (slightly beyond the 5% acceptance tolerance). The two existing alternating blocks pass their numeric gates, but these control measurements do not support claiming uniformly stable timing or statistical significance. No control run was discarded or retuned.

| Case | Main A→B spread | A five-run range | B five-run range |
|---|---:|---:|---:|
| 1 tag | +0.37% | 522.41–573.87 | 550.78–564.67 |
| 5 tags | +32.91% | 154.64–198.93 | 198.70–213.10 |
| 20 tags | -1.16% | 60.83–65.34 | 62.65–65.05 |
| shared only | -5.07% | 639.99–658.87 | 599.86–626.67 |
| shared+4 | -1.39% | 301.62–302.71 | 291.99–306.63 |
| disjoint 5 | +4.76% | 1041.96–1076.34 | 966.11–1283.70 |

### Every measured throughput run

| Case | Block | Main runs 1–5 | Branch (control B) runs 1–5 |
|---|---|---|---|
| 1 tag | 1 | 564.91, 540.01, 542.97, 555.35, 552.30 | 552.76, 544.14, 557.44, 568.11, 569.90 |
| 1 tag | 2 | 555.52, 553.92, 553.76, 546.68, 560.73 | 541.22, 540.17, 542.50, 536.55, 544.83 |
| 1 tag | control | 554.66, 522.41, 573.87, 565.17, 551.64 | 550.78, 556.45, 556.72, 564.67, 560.68 |
| 5 tags | 1 | 214.28, 207.74, 211.35, 213.33, 213.86 | 438.13, 437.13, 440.64, 449.40, 456.01 |
| 5 tags | 2 | 217.59, 215.92, 216.69, 215.94, 211.48 | 438.71, 400.73, 425.93, 397.19, 406.73 |
| 5 tags | control | 190.67, 154.64, 158.14, 160.15, 198.93 | 198.70, 213.10, 212.92, 212.86, 206.38 |
| 20 tags | 1 | 63.42, 64.29, 64.24, 63.51, 62.82 | 153.99, 173.36, 149.33, 134.39, 141.56 |
| 20 tags | 2 | 65.30, 64.89, 65.28, 64.09, 63.67 | 216.83, 237.16, 184.98, 241.15, 240.59 |
| 20 tags | control | 62.25, 60.83, 65.34, 65.09, 64.85 | 62.65, 64.09, 63.85, 64.64, 65.05 |
| shared only | 1 | 635.64, 632.12, 622.86, 628.26, 640.32 | 622.68, 637.94, 637.57, 641.92, 631.10 |
| shared only | 2 | 644.74, 639.95, 651.35, 640.25, 626.58 | 642.28, 612.20, 630.64, 632.57, 633.23 |
| shared only | control | 646.67, 639.99, 652.62, 658.87, 655.74 | 599.86, 619.54, 613.06, 626.11, 626.67 |
| shared+4 | 1 | 304.65, 306.94, 302.14, 308.11, 307.34 | 544.14, 552.66, 548.03, 546.69, 547.11 |
| shared+4 | 2 | 305.67, 307.06, 305.94, 299.84, 301.86 | 550.06, 539.71, 548.75, 546.30, 551.22 |
| shared+4 | control | 302.13, 301.86, 302.71, 301.62, 302.36 | 306.63, 291.99, 301.24, 293.80, 297.92 |
| disjoint 5 | 1 | 1314.97, 1311.36, 1318.41, 1321.27, 1328.06 | 2865.00, 2896.34, 2892.96, 2901.28, 2873.34 |
| disjoint 5 | 2 | 1378.95, 1386.18, 1355.77, 1328.93, 1346.54 | 2919.67, 2882.85, 2883.18, 2881.66, 2858.14 |
| disjoint 5 | control | 1052.56, 1041.96, 1066.51, 1054.83, 1076.34 | 1279.86, 1283.70, 966.11, 1026.38, 1105.01 |

Every individual latency/count is in the six raw JSONL outputs (rev4 main/branch r1/r2; rev5 main control 1/2). Previous detailed tables follow unchanged below.

## Long-history EXPLAIN (ANALYZE, BUFFERS)

Two requested tags each have **10,000 rows** (20,000 history rows total), using the existing composite `(ServiceId, Tag, SortableUniqueId)` index. SQL is extracted verbatim from production and run in transactions that roll back. Both custom/generic plans and fresh/analyzed statistics use backward index-only scans under LIMIT 1, actual one history row per requested key (`rows=1 loops=2`), without scanning/sorting the 10k history. Heap fetches: 2 for each history scan in this unvacuumed fixture. Bootstrap also updates the two head rows; reconciliation is read-only. These are diagnostic execution times, not application latency.

| Statistics / plan | Bootstrap execution ms | Reconciliation execution ms | Bootstrap buffers | Reconciliation buffers |
|---|---:|---:|---|---|
| [fresh/custom](plans/rev5-long-history-fresh-custom.txt) | 0.186 | 0.047 | shared hit=16 | shared hit=8 |
| [fresh/generic](plans/rev5-long-history-fresh-generic.txt) | 0.165 | 0.043 | shared hit=13 | shared hit=8 |
| [analyzed/custom](plans/rev5-long-history-analyzed-custom.txt) | 0.158 | 0.047 | shared hit=13 | shared hit=8 |
| [analyzed/generic](plans/rev5-long-history-analyzed-generic.txt) | 0.171 | 0.055 | shared hit=13 | shared hit=8 |

Exact bootstrap and reconciliation SQL and full `EXPLAIN (ANALYZE, BUFFERS)` (fresh/custom representative; other plans linked above):

```text
Pager usage is off.
SET
BOOTSTRAP: UPDATE dcb_tag_heads h SET "HeadPosition" = m.position FROM (SELECT k.service_id, k.tag, m.position FROM unnest(@serviceIds, @tags) WITH ORDINALITY AS k(service_id, tag, ord) CROSS JOIN LATERAL (SELECT t."ServiceId" AS service_id, t."Tag" AS tag, t."SortableUniqueId" AS position FROM dcb_tags t WHERE (t."ServiceId", t."Tag") <= (k.service_id, k.tag) ORDER BY t."ServiceId" DESC, t."Tag" DESC, t."SortableUniqueId" DESC LIMIT 1) m WHERE m.service_id = k.service_id AND m.tag = k.tag) m WHERE h."ServiceId" = m.service_id AND h."Tag" = m.tag AND m.position IS NOT NULL RETURNING h."ServiceId", h."Tag", h."HeadPosition"
BEGIN
PREPARE
                                                                                              QUERY PLAN
-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
 Update on dcb_tag_heads h  (cost=0.56..11.09 rows=1 width=732) (actual time=0.071..0.086 rows=2 loops=1)
   Buffers: shared hit=16
   ->  Nested Loop  (cost=0.56..11.09 rows=1 width=732) (actual time=0.057..0.071 rows=2 loops=1)
         Join Filter: (((h."ServiceId")::text = (m.service_id)::text) AND (h."Tag" = m.tag))
         Buffers: shared hit=12
         ->  Nested Loop  (cost=0.41..2.91 rows=1 width=968) (actual time=0.051..0.062 rows=2 loops=1)
               Buffers: shared hit=8
               ->  Function Scan on k  (cost=0.01..0.03 rows=2 width=152) (actual time=0.011..0.011 rows=2 loops=1)
               ->  Subquery Scan on m  (cost=0.41..1.43 rows=1 width=816) (actual time=0.025..0.025 rows=1 loops=2)
                     Filter: ((m."position" IS NOT NULL) AND (k.service_id = (m.service_id)::text) AND (k.tag = m.tag))
                     Buffers: shared hit=8
                     ->  Limit  (cost=0.41..1.42 rows=1 width=396) (actual time=0.024..0.024 rows=1 loops=2)
                           Buffers: shared hit=8
                           ->  Index Only Scan Backward using "IX_Tags_Service_Tag_SortableUniqueId" on dcb_tags t  (cost=0.41..1441.43 rows=1430 width=396) (actual time=0.023..0.023 rows=1 loops=2)
                                 Index Cond: (ROW("ServiceId", "Tag") <= ROW(k.service_id, k.tag))
                                 Heap Fetches: 2
                                 Buffers: shared hit=8
         ->  Index Scan using dcb_tag_heads_pkey on dcb_tag_heads h  (cost=0.14..8.16 rows=1 width=184) (actual time=0.003..0.003 rows=1 loops=2)
               Index Cond: ((("ServiceId")::text = k.service_id) AND ("Tag" = k.tag))
               Buffers: shared hit=4
 Planning:
   Buffers: shared hit=202 read=8
 Planning Time: 0.737 ms
 Execution Time: 0.186 ms
(24 rows)

DEALLOCATE
ROLLBACK
RECONCILIATION: SELECT k.service_id, k.tag, m.position FROM unnest(@serviceIds, @tags, @positions) WITH ORDINALITY AS k(service_id, tag, position, ord) CROSS JOIN LATERAL (SELECT t."ServiceId" AS service_id, t."Tag" AS tag, t."SortableUniqueId" AS position FROM dcb_tags t WHERE (t."ServiceId", t."Tag") <= (k.service_id, k.tag) ORDER BY t."ServiceId" DESC, t."Tag" DESC, t."SortableUniqueId" DESC LIMIT 1) m WHERE m.service_id = k.service_id AND m.tag = k.tag AND (k.position IS NULL OR m.position > k.position) ORDER BY k.ord
BEGIN
PREPARE
                                                                                        QUERY PLAN
-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
 Nested Loop  (cost=0.41..2.91 rows=1 width=290) (actual time=0.025..0.033 rows=2 loops=1)
   Buffers: shared hit=8
   ->  Function Scan on k  (cost=0.01..0.03 rows=2 width=104) (actual time=0.011..0.012 rows=2 loops=1)
   ->  Subquery Scan on m  (cost=0.41..1.43 rows=1 width=396) (actual time=0.010..0.010 rows=1 loops=2)
         Filter: (((k."position" IS NULL) OR ((m."position")::text > k."position")) AND (k.service_id = (m.service_id)::text) AND (k.tag = m.tag))
         Buffers: shared hit=8
         ->  Limit  (cost=0.41..1.42 rows=1 width=396) (actual time=0.009..0.009 rows=1 loops=2)
               Buffers: shared hit=8
               ->  Index Only Scan Backward using "IX_Tags_Service_Tag_SortableUniqueId" on dcb_tags t  (cost=0.41..1441.43 rows=1430 width=396) (actual time=0.009..0.009 rows=1 loops=2)
                     Index Cond: (ROW("ServiceId", "Tag") <= ROW(k.service_id, k.tag))
                     Heap Fetches: 2
                     Buffers: shared hit=8
 Planning:
   Buffers: shared hit=5
 Planning Time: 0.079 ms
 Execution Time: 0.047 ms
(16 rows)

DEALLOCATE
ROLLBACK
```

<!-- REV4_HISTORY -->
# SEK-G162 packet rev 4 — PASS

Measured 2026-11-13 against `origin/main` / `e1478efefc578db03e68b06d66a1898270530db0`. Both blocks are evaluated independently; a gate passes only if both blocks pass. All five gates pass.

## Implementation and path selection

The single canonical seam selects SQL from the batch's **total distinct-key count** once. Exactly one key uses main's original per-key SQL text and parameters for lazy insert, locking, bootstrap, reconciliation, audit, repair/advance and cleanup. Two or more keys use the existing set-based statements with bounded backward top-1 predecessor seeks. A one-row repair or advance subset in a multi-key batch still uses set-based SQL. Zero-key batches follow main's empty per-key loops and send no head SQL. There is no duplicate seam. Phase structure, all six hook points, Phase 4 C# expectation/position logic, exceptions and results remain intact. Source comparison evidence (local evidence, not committed) checks the copied per-key phase bodies, scalar/parameter/audit/advance helpers and six hook calls against main.

`PostgresEventStore.cs` and `PostgresSetBasedAppendTests.cs` contain the implementation/test changes. The benchmark README, this report, `run_alternating.py`, rev-4 raw results and validation evidence were updated; the benchmark project and solution registration from earlier attempts remain. `Program.cs`, workloads, connections, seeding, truncation, warm-up and five-second duration are unchanged. No production schema/index/migration change was made.

## Correctness and statement counts

After the zero-key guard, every previous correctness suite was rerun on the final source, before timing. Full solution Release rebuild on both TFMs: **365 warnings, 0 errors** (log (local evidence, not committed)). No branch failures or skips.

| Suite | net9.0 | net10.0 |
|---|---:|---:|
| Full `Sekiban.Dcb.Postgres.Tests` | 130 passed | 130 passed |
| `Sekiban.Dcb.WithResult.Tests`, filter `FullyQualifiedName~Postgres\|FullyQualifiedName~ConditionalAppend` | 141 passed | 141 passed |
| `Sekiban.Dcb.Orleans.Tests`, filter `FullyQualifiedName~TwoClusterPostgresProductTests` | 12 passed | 12 passed |
| Main statement-count probe | 1 expected failure | 1 expected failure |

Commands use `dotnet test dcb/tests/<project> -c Release -p:GenerateSBOM=false --no-build --no-restore --logger 'console;verbosity=detailed'` plus the filter where shown. Logs: PostgreSQL (local evidence, not committed), WithResult (local evidence, not committed), Orleans (local evidence, not committed), main count probe (local evidence, not committed). Main probe builds against the swapped main store before timing and deliberately fails the multi-key constant-count assertion after printing every count on both TFMs.

| Scenario | Implementation | 1 | 2 | 5 | 20 |
|---|---|---:|---:|---:|---:|
| Success, actual distinct keys | main | 4 | 8 | 20 | 80 |
| Success, actual distinct keys | branch | 4 | 4 | 4 | 4 |
| Repair-only conflict, actual distinct keys | main | 5 | 10 | 25 | 100 |
| Repair-only conflict, actual distinct keys | branch | 5 | 5 | 5 | 5 |
| Historical repair+cleanup, repaired tags (total keys = twice label) | main | 10 | 20 | 50 | 200 |
| Historical repair+cleanup, repaired tags (total keys = twice label) | branch | 7 | 7 | 7 | 7 |

The old report's “1-tag repair-only” count of 10 on main was a **two-key** fixture: one repaired key plus one unrelated lazy key. A true one-key repair-only conflict is 5 statements on main and branch. The historical fixture remains to preserve main-growth evidence and cleanup coverage. Counts agree on both TFMs, exclude EF event/tag DML and epoch checks, and count only raw head-protocol statements.

Path coverage:

- **Per-key:** one-key cases in `PostgresTagHeadCasTests`, including `Bootstrap_ExistingAuthoritativeTagHistoryIsNotMistakenForAssertEmpty`, `Reconciliation_DetectsAuthoritativeBypassMaximaAcrossTheWholeBatchRange`, `ReconciliationAudit_RollsBackOnFailure_ThenRemainsIdempotentAndAppendOnlyOnRetry`, `PartialStateFailures_AfterEachProductionStage_RollBackFromFreshConnection`, and the one-tag races now execute main SQL. The 1-key success/repair count fixtures assert 4/5 and absence of `unnest` in every phase.
- **Set-based:** `ManyTags_LazyExistingAndRepaired_PreserveHeadsAndAudit` (24 keys), `MissingKeys_DoNotBorrowPredecessorHeadsOrRepairs` (3 keys), `ReversedUnicodeBatches_DoNotDeadlock` (5 keys), `OrdinalOrder_DirectBlockingProbe` (2 keys, four variants), existing multi-tag CAS cases, and 2/5/20-key count fixtures. Count fixtures assert `unnest` in every raw phase statement.
- **Mixed paths:** `SingleAndMultiKeyWriters_SharedKey_BothOrdersCompleteWithout40P01` runs one-key-first and multi-key-first against both lazy and existing heads (four cases). It pauses writer 1 after Phase 2, observes writer 2 waiting on the shared key, releases writer 1, and requires both results to succeed within the timeout with both events durable and shared head `0020`. A returned 40P01 fails the success assertion.
- **Zero keys:** `UntaggedBatch_SendsNoHeadSql_AndKeepsAllSixHooks` checks no head SQL, six hooks and a durable event.

## Alternating acceptance benchmark

Execution order: **main-1 → branch-1 → main-2 → branch-2**. Every invocation creates its own PostgreSQL 16 container and runs six cases, with one warm-up and five measured five-second runs per case. There were no concurrent builds, tests, SQL observers or profilers during timing. A temporary-file backup is restored byte-for-byte in `finally` after every invocation; SHA-256 and restoration evidence (local evidence, not committed). Runtime/machine and harness are the same as the second attempt below (.NET 10.0.12, Apple M4 Max, macOS 27.2.1). [Reproduction](README.md).

Each run column lists successful appends/s in execution order. Medians and ratios are calculated from unrounded values. Gates: ≥95% for one tag/shared-only; ≥110% for five tags/20 tags/shared+4. Disjoint has no threshold.

| Case | Block | Main runs 1–5 | Branch runs 1–5 | Main median | Branch median | Branch/main | Gate |
|---|---:|---|---|---:|---:|---:|---|
| 1 tag | 1 | 564.91, 540.01, 542.97, 555.35, 552.30 | 552.76, 544.14, 557.44, 568.11, 569.90 | 552.30 | 557.44 | 100.9% | PASS |
| 1 tag | 2 | 555.52, 553.92, 553.76, 546.68, 560.73 | 541.22, 540.17, 542.50, 536.55, 544.83 | 553.92 | 541.22 | 97.7% | PASS |
| 5 tags | 1 | 214.28, 207.74, 211.35, 213.33, 213.86 | 438.13, 437.13, 440.64, 449.40, 456.01 | 213.33 | 440.64 | 206.6% | PASS |
| 5 tags | 2 | 217.59, 215.92, 216.69, 215.94, 211.48 | 438.71, 400.73, 425.93, 397.19, 406.73 | 215.94 | 406.73 | 188.3% | PASS |
| 20 tags | 1 | 63.42, 64.29, 64.24, 63.51, 62.82 | 153.99, 173.36, 149.33, 134.39, 141.56 | 63.51 | 149.33 | 235.1% | PASS |
| 20 tags | 2 | 65.30, 64.89, 65.28, 64.09, 63.67 | 216.83, 237.16, 184.98, 241.15, 240.59 | 64.89 | 237.16 | 365.5% | PASS |
| shared only | 1 | 635.64, 632.12, 622.86, 628.26, 640.32 | 622.68, 637.94, 637.57, 641.92, 631.10 | 632.12 | 637.57 | 100.9% | PASS |
| shared only | 2 | 644.74, 639.95, 651.35, 640.25, 626.58 | 642.28, 612.20, 630.64, 632.57, 633.23 | 640.25 | 632.57 | 98.8% | PASS |
| shared+4 | 1 | 304.65, 306.94, 302.14, 308.11, 307.34 | 544.14, 552.66, 548.03, 546.69, 547.11 | 306.94 | 547.11 | 178.2% | PASS |
| shared+4 | 2 | 305.67, 307.06, 305.94, 299.84, 301.86 | 550.06, 539.71, 548.75, 546.30, 551.22 | 305.67 | 548.75 | 179.5% | PASS |
| disjoint 5 | 1 | 1314.97, 1311.36, 1318.41, 1321.27, 1328.06 | 2865.00, 2896.34, 2892.96, 2901.28, 2873.34 | 1318.41 | 2892.96 | 219.4% | reported |
| disjoint 5 | 2 | 1378.95, 1386.18, 1355.77, 1328.93, 1346.54 | 2919.67, 2882.85, 2883.18, 2881.66, 2858.14 | 1355.77 | 2882.85 | 212.6% | reported |

| Case | Both blocks |
|---|---|
| 1 tag | PASS |
| 5 tags | PASS |
| 20 tags | PASS |
| shared only | PASS |
| shared+4 | PASS |
| disjoint 5 | reported |

## Latency and successful writes

p50/p99 are medians of five per-run percentiles, in milliseconds. Counts exclude warm-ups/seeding. Every individual run follows in the full-run table. All writes succeeded; the harness terminates on any failure.

| Case | Block | Main p50/p99 ms | Branch p50/p99 ms | Successful writes main/branch |
|---|---:|---:|---:|---:|
| 1 tag | 1 | 1.773/2.397 | 1.758/2.357 | 13781/13965 |
| 1 tag | 2 | 1.759/2.412 | 1.816/2.369 | 13856/13530 |
| 5 tags | 1 | 4.585/5.689 | 2.200/2.954 | 5305/11110 |
| 5 tags | 2 | 4.532/5.605 | 2.401/3.176 | 5390/10349 |
| 20 tags | 1 | 15.546/18.134 | 4.457/43.555 | 1595/3768 |
| 20 tags | 2 | 15.178/17.756 | 3.995/9.799 | 1619/5605 |
| shared only | 1 | 24.865/30.799 | 24.863/32.920 | 15872/15930 |
| shared only | 2 | 24.734/29.068 | 25.141/29.553 | 16093/15826 |
| shared+4 | 1 | 51.747/62.840 | 28.708/39.092 | 7718/13769 |
| shared+4 | 2 | 51.825/59.205 | 28.844/34.431 | 7675/13753 |
| disjoint 5 | 1 | 11.705/22.890 | 5.018/15.764 | 33034/72218 |
| disjoint 5 | 2 | 11.446/21.875 | 5.126/11.268 | 34046/72198 |

Raw files: main-1 (local evidence, not committed), branch-1 (local evidence, not committed), main-2 (local evidence, not committed), branch-2 (local evidence, not committed).

## Interrupted pre-verification sequence

One initial main invocation completed before review found that zero-key batches still dispatched to array SQL. The following branch invocation was interrupted before any warm-up or measured sample (its file contains only the runtime header). The code was corrected, the untagged regression added, all suites rerun, then the complete acceptance sequence above started fresh. No timing parameter or workload changed; no result was discarded to rescue a gate. This preliminary main invocation is reported separately and is not mixed into acceptance ratios. Raw files: completed preliminary main (local evidence, not committed), branch header (local evidence, not committed), interrupted restoration record (local evidence, not committed).

| Case | Preliminary main runs 1–5 (appends/s) | Median |
|---|---|---:|
| 1 tag | 448.99, 452.58, 472.37, 420.10, 496.01 | 452.58 |
| 5 tags | 208.42, 210.62, 205.14, 213.90, 214.71 | 210.62 |
| 20 tags | 64.01, 63.11, 63.75, 64.38, 64.67 | 64.01 |
| shared only | 636.79, 624.33, 624.60, 627.72, 639.85 | 627.72 |
| shared+4 | 301.37, 303.11, 302.72, 307.22, 306.51 | 303.11 |
| disjoint 5 | 1325.98, 1244.33, 1158.87, 1173.56, 1166.79 | 1173.56 |

## Every run, including warm-ups

`Run 0` is the excluded warm-up. Acceptance invocations are listed in execution order, followed by the preliminary main invocation. Throughput is appends/s; latency is ms. Raw JSON retains full precision.

| Invocation | Case | Run | Throughput | p50 | p99 | Successful writes |
|---|---|---:|---:|---:|---:|---:|
| main-1 | 1 tag | 0 | 468.28 | 2.0995 | 3.4239 | 2343 |
| main-1 | 1 tag | 1 | 564.91 | 1.7302 | 2.4081 | 2825 |
| main-1 | 1 tag | 2 | 540.01 | 1.8099 | 2.3972 | 2701 |
| main-1 | 1 tag | 3 | 542.97 | 1.8011 | 2.4310 | 2715 |
| main-1 | 1 tag | 4 | 555.35 | 1.7647 | 2.2577 | 2778 |
| main-1 | 1 tag | 5 | 552.30 | 1.7730 | 2.3384 | 2762 |
| main-1 | 5 tags | 0 | 215.06 | 4.5585 | 5.5060 | 1076 |
| main-1 | 5 tags | 1 | 214.28 | 4.5870 | 5.6895 | 1072 |
| main-1 | 5 tags | 2 | 207.74 | 4.7259 | 6.0004 | 1039 |
| main-1 | 5 tags | 3 | 211.35 | 4.5551 | 6.6142 | 1057 |
| main-1 | 5 tags | 4 | 213.33 | 4.5855 | 5.6235 | 1067 |
| main-1 | 5 tags | 5 | 213.86 | 4.5820 | 5.5638 | 1070 |
| main-1 | 20 tags | 0 | 63.15 | 15.5871 | 18.4395 | 316 |
| main-1 | 20 tags | 1 | 63.42 | 15.5465 | 17.9784 | 318 |
| main-1 | 20 tags | 2 | 64.29 | 15.3290 | 17.9795 | 322 |
| main-1 | 20 tags | 3 | 64.24 | 15.2937 | 18.3816 | 322 |
| main-1 | 20 tags | 4 | 63.51 | 15.6002 | 18.1342 | 318 |
| main-1 | 20 tags | 5 | 62.82 | 15.5842 | 22.9016 | 315 |
| main-1 | shared only | 0 | 633.61 | 24.8035 | 29.9472 | 3183 |
| main-1 | shared only | 1 | 635.64 | 24.8536 | 29.2705 | 3193 |
| main-1 | shared only | 2 | 632.12 | 24.8651 | 31.2883 | 3175 |
| main-1 | shared only | 3 | 622.86 | 25.2592 | 35.7022 | 3130 |
| main-1 | shared only | 4 | 628.26 | 25.1697 | 30.7985 | 3157 |
| main-1 | shared only | 5 | 640.32 | 24.5681 | 30.1390 | 3217 |
| main-1 | shared+4 | 0 | 301.81 | 52.0152 | 73.9058 | 1524 |
| main-1 | shared+4 | 1 | 304.65 | 51.8201 | 63.8005 | 1538 |
| main-1 | shared+4 | 2 | 306.94 | 51.7471 | 57.4801 | 1549 |
| main-1 | shared+4 | 3 | 302.14 | 52.4169 | 66.4938 | 1525 |
| main-1 | shared+4 | 4 | 308.11 | 51.5038 | 60.6242 | 1555 |
| main-1 | shared+4 | 5 | 307.34 | 51.5680 | 62.8397 | 1551 |
| main-1 | disjoint 5 | 0 | 1341.77 | 11.5189 | 22.5917 | 6722 |
| main-1 | disjoint 5 | 1 | 1314.97 | 11.7557 | 22.1180 | 6586 |
| main-1 | disjoint 5 | 2 | 1311.36 | 11.7191 | 23.4072 | 6570 |
| main-1 | disjoint 5 | 3 | 1318.41 | 11.7050 | 22.8900 | 6604 |
| main-1 | disjoint 5 | 4 | 1321.27 | 11.6453 | 23.9330 | 6621 |
| main-1 | disjoint 5 | 5 | 1328.06 | 11.6857 | 21.3522 | 6653 |
| branch-1 | 1 tag | 0 | 483.91 | 1.9582 | 3.3077 | 2421 |
| branch-1 | 1 tag | 1 | 552.76 | 1.7784 | 2.3970 | 2765 |
| branch-1 | 1 tag | 2 | 544.14 | 1.7951 | 2.3573 | 2721 |
| branch-1 | 1 tag | 3 | 557.44 | 1.7583 | 2.3225 | 2788 |
| branch-1 | 1 tag | 4 | 568.11 | 1.7198 | 2.4425 | 2841 |
| branch-1 | 1 tag | 5 | 569.90 | 1.7245 | 2.2535 | 2850 |
| branch-1 | 5 tags | 0 | 434.38 | 2.2463 | 3.1457 | 2172 |
| branch-1 | 5 tags | 1 | 438.13 | 2.2218 | 2.9951 | 2191 |
| branch-1 | 5 tags | 2 | 437.13 | 2.2215 | 3.0581 | 2186 |
| branch-1 | 5 tags | 3 | 440.64 | 2.1996 | 2.9544 | 2204 |
| branch-1 | 5 tags | 4 | 449.40 | 2.1655 | 2.9010 | 2248 |
| branch-1 | 5 tags | 5 | 456.01 | 2.1473 | 2.7543 | 2281 |
| branch-1 | 20 tags | 0 | 170.65 | 3.9835 | 45.1464 | 854 |
| branch-1 | 20 tags | 1 | 153.99 | 4.3817 | 43.5553 | 770 |
| branch-1 | 20 tags | 2 | 173.36 | 3.9752 | 43.3668 | 869 |
| branch-1 | 20 tags | 3 | 149.33 | 4.4567 | 40.6416 | 747 |
| branch-1 | 20 tags | 4 | 134.39 | 4.6277 | 50.9201 | 674 |
| branch-1 | 20 tags | 5 | 141.56 | 4.6563 | 44.0559 | 708 |
| branch-1 | shared only | 0 | 622.21 | 25.2534 | 31.4964 | 3127 |
| branch-1 | shared only | 1 | 622.68 | 25.4359 | 32.9200 | 3128 |
| branch-1 | shared only | 2 | 637.94 | 24.8630 | 29.9072 | 3205 |
| branch-1 | shared only | 3 | 637.57 | 24.8153 | 33.5088 | 3202 |
| branch-1 | shared only | 4 | 641.92 | 24.6786 | 29.6098 | 3225 |
| branch-1 | shared only | 5 | 631.10 | 24.8951 | 33.5679 | 3170 |
| branch-1 | shared+4 | 0 | 537.75 | 29.2746 | 39.0411 | 2703 |
| branch-1 | shared+4 | 1 | 544.14 | 28.9577 | 36.7580 | 2735 |
| branch-1 | shared+4 | 2 | 552.66 | 28.6355 | 33.5448 | 2778 |
| branch-1 | shared+4 | 3 | 548.03 | 28.5984 | 39.0923 | 2756 |
| branch-1 | shared+4 | 4 | 546.69 | 28.7080 | 39.8004 | 2749 |
| branch-1 | shared+4 | 5 | 547.11 | 28.7512 | 39.9175 | 2751 |
| branch-1 | disjoint 5 | 0 | 2855.46 | 5.1006 | 15.3585 | 14289 |
| branch-1 | disjoint 5 | 1 | 2865.00 | 5.0838 | 15.1980 | 14343 |
| branch-1 | disjoint 5 | 2 | 2896.34 | 5.0160 | 15.5990 | 14494 |
| branch-1 | disjoint 5 | 3 | 2892.96 | 5.0178 | 15.7883 | 14481 |
| branch-1 | disjoint 5 | 4 | 2901.28 | 5.0017 | 15.8667 | 14520 |
| branch-1 | disjoint 5 | 5 | 2873.34 | 5.0435 | 15.7637 | 14380 |
| main-2 | 1 tag | 0 | 464.67 | 2.0108 | 4.2961 | 2325 |
| main-2 | 1 tag | 1 | 555.52 | 1.7538 | 2.4116 | 2779 |
| main-2 | 1 tag | 2 | 553.92 | 1.7589 | 2.4650 | 2770 |
| main-2 | 1 tag | 3 | 553.76 | 1.7604 | 2.4011 | 2769 |
| main-2 | 1 tag | 4 | 546.68 | 1.7819 | 2.4384 | 2734 |
| main-2 | 1 tag | 5 | 560.73 | 1.7482 | 2.2820 | 2804 |
| main-2 | 5 tags | 0 | 216.47 | 4.5030 | 6.1840 | 1083 |
| main-2 | 5 tags | 1 | 217.59 | 4.4968 | 5.5580 | 1088 |
| main-2 | 5 tags | 2 | 215.92 | 4.5316 | 5.9520 | 1080 |
| main-2 | 5 tags | 3 | 216.69 | 4.5307 | 5.5076 | 1084 |
| main-2 | 5 tags | 4 | 215.94 | 4.5397 | 5.6048 | 1080 |
| main-2 | 5 tags | 5 | 211.48 | 4.6480 | 5.7082 | 1058 |
| main-2 | 20 tags | 0 | 64.75 | 15.2159 | 18.6955 | 324 |
| main-2 | 20 tags | 1 | 65.30 | 15.1066 | 17.6025 | 327 |
| main-2 | 20 tags | 2 | 64.89 | 15.0884 | 18.1875 | 325 |
| main-2 | 20 tags | 3 | 65.28 | 15.1777 | 17.2407 | 327 |
| main-2 | 20 tags | 4 | 64.09 | 15.4235 | 18.1436 | 321 |
| main-2 | 20 tags | 5 | 63.67 | 15.6297 | 17.7557 | 319 |
| main-2 | shared only | 0 | 633.17 | 24.8879 | 31.2993 | 3180 |
| main-2 | shared only | 1 | 644.74 | 24.5801 | 28.2301 | 3241 |
| main-2 | shared only | 2 | 639.95 | 24.7546 | 29.4594 | 3217 |
| main-2 | shared only | 3 | 651.35 | 24.3474 | 28.0527 | 3271 |
| main-2 | shared only | 4 | 640.25 | 24.7336 | 29.0682 | 3216 |
| main-2 | shared only | 5 | 626.58 | 25.3268 | 29.1545 | 3148 |
| main-2 | shared+4 | 0 | 304.27 | 51.8643 | 65.0393 | 1536 |
| main-2 | shared+4 | 1 | 305.67 | 51.7777 | 60.1255 | 1543 |
| main-2 | shared+4 | 2 | 307.06 | 51.6975 | 57.6655 | 1551 |
| main-2 | shared+4 | 3 | 305.94 | 51.8255 | 62.5933 | 1544 |
| main-2 | shared+4 | 4 | 299.84 | 53.1832 | 59.2050 | 1514 |
| main-2 | shared+4 | 5 | 301.86 | 52.6290 | 59.0698 | 1523 |
| main-2 | disjoint 5 | 0 | 1384.41 | 11.1896 | 21.3332 | 6932 |
| main-2 | disjoint 5 | 1 | 1378.95 | 11.0787 | 24.6362 | 6908 |
| main-2 | disjoint 5 | 2 | 1386.18 | 11.1536 | 21.9248 | 6947 |
| main-2 | disjoint 5 | 3 | 1355.77 | 11.4462 | 21.8212 | 6790 |
| main-2 | disjoint 5 | 4 | 1328.93 | 11.6634 | 21.8750 | 6656 |
| main-2 | disjoint 5 | 5 | 1346.54 | 11.4843 | 21.0286 | 6745 |
| branch-2 | 1 tag | 0 | 472.25 | 2.0022 | 3.4318 | 2363 |
| branch-2 | 1 tag | 1 | 541.22 | 1.8280 | 2.3688 | 2707 |
| branch-2 | 1 tag | 2 | 540.17 | 1.8087 | 2.4000 | 2701 |
| branch-2 | 1 tag | 3 | 542.50 | 1.8034 | 2.3602 | 2713 |
| branch-2 | 1 tag | 4 | 536.55 | 1.8241 | 2.5098 | 2684 |
| branch-2 | 1 tag | 5 | 544.83 | 1.8160 | 2.2993 | 2725 |
| branch-2 | 5 tags | 0 | 446.41 | 2.1936 | 2.9560 | 2233 |
| branch-2 | 5 tags | 1 | 438.71 | 2.2349 | 2.9093 | 2194 |
| branch-2 | 5 tags | 2 | 400.73 | 2.4686 | 3.3857 | 2004 |
| branch-2 | 5 tags | 3 | 425.93 | 2.2990 | 3.0892 | 2130 |
| branch-2 | 5 tags | 4 | 397.19 | 2.5150 | 3.3681 | 1987 |
| branch-2 | 5 tags | 5 | 406.73 | 2.4015 | 3.1756 | 2034 |
| branch-2 | 20 tags | 0 | 209.88 | 3.8494 | 20.7447 | 1050 |
| branch-2 | 20 tags | 1 | 216.83 | 4.0047 | 17.6954 | 1085 |
| branch-2 | 20 tags | 2 | 237.16 | 3.9947 | 9.7994 | 1186 |
| branch-2 | 20 tags | 3 | 184.98 | 4.1018 | 35.2447 | 925 |
| branch-2 | 20 tags | 4 | 241.15 | 3.9540 | 9.7154 | 1206 |
| branch-2 | 20 tags | 5 | 240.59 | 3.9755 | 8.4575 | 1203 |
| branch-2 | shared only | 0 | 627.74 | 25.1734 | 34.3361 | 3153 |
| branch-2 | shared only | 1 | 642.28 | 24.7079 | 29.1677 | 3225 |
| branch-2 | shared only | 2 | 612.20 | 25.9260 | 32.2321 | 3075 |
| branch-2 | shared only | 3 | 630.64 | 25.1919 | 28.7621 | 3167 |
| branch-2 | shared only | 4 | 632.57 | 25.1407 | 29.5528 | 3177 |
| branch-2 | shared only | 5 | 633.23 | 25.0225 | 32.2310 | 3182 |
| branch-2 | shared+4 | 0 | 548.01 | 28.7322 | 37.5762 | 2754 |
| branch-2 | shared+4 | 1 | 550.06 | 28.8855 | 33.8536 | 2765 |
| branch-2 | shared+4 | 2 | 539.71 | 29.2451 | 42.0877 | 2712 |
| branch-2 | shared+4 | 3 | 548.75 | 28.8104 | 34.4308 | 2759 |
| branch-2 | shared+4 | 4 | 546.30 | 28.8442 | 36.0765 | 2746 |
| branch-2 | shared+4 | 5 | 551.22 | 28.7210 | 33.7848 | 2771 |
| branch-2 | disjoint 5 | 0 | 2922.62 | 5.0863 | 10.7764 | 14628 |
| branch-2 | disjoint 5 | 1 | 2919.67 | 5.0924 | 10.9642 | 14613 |
| branch-2 | disjoint 5 | 2 | 2882.85 | 5.1390 | 11.2124 | 14427 |
| branch-2 | disjoint 5 | 3 | 2883.18 | 5.1160 | 11.6859 | 14431 |
| branch-2 | disjoint 5 | 4 | 2881.66 | 5.1257 | 11.2675 | 14424 |
| branch-2 | disjoint 5 | 5 | 2858.14 | 5.1563 | 11.6575 | 14303 |
| preliminary-main-1 | 1 tag | 0 | 373.26 | 2.5894 | 4.6254 | 1869 |
| preliminary-main-1 | 1 tag | 1 | 448.99 | 2.1310 | 3.3374 | 2246 |
| preliminary-main-1 | 1 tag | 2 | 452.58 | 2.1716 | 3.1266 | 2263 |
| preliminary-main-1 | 1 tag | 3 | 472.37 | 2.0638 | 3.0720 | 2362 |
| preliminary-main-1 | 1 tag | 4 | 420.10 | 2.3055 | 3.7117 | 2101 |
| preliminary-main-1 | 1 tag | 5 | 496.01 | 1.9773 | 2.8902 | 2481 |
| preliminary-main-1 | 5 tags | 0 | 210.55 | 4.7057 | 5.7265 | 1053 |
| preliminary-main-1 | 5 tags | 1 | 208.42 | 4.7418 | 5.8730 | 1043 |
| preliminary-main-1 | 5 tags | 2 | 210.62 | 4.6600 | 6.1402 | 1054 |
| preliminary-main-1 | 5 tags | 3 | 205.14 | 4.7206 | 7.2304 | 1026 |
| preliminary-main-1 | 5 tags | 4 | 213.90 | 4.5575 | 5.6985 | 1070 |
| preliminary-main-1 | 5 tags | 5 | 214.71 | 4.5282 | 5.8401 | 1074 |
| preliminary-main-1 | 20 tags | 0 | 64.47 | 15.2798 | 17.9619 | 323 |
| preliminary-main-1 | 20 tags | 1 | 64.01 | 15.3244 | 18.8189 | 321 |
| preliminary-main-1 | 20 tags | 2 | 63.11 | 15.7203 | 18.5085 | 316 |
| preliminary-main-1 | 20 tags | 3 | 63.75 | 15.4562 | 18.5367 | 319 |
| preliminary-main-1 | 20 tags | 4 | 64.38 | 15.3245 | 18.0980 | 322 |
| preliminary-main-1 | 20 tags | 5 | 64.67 | 15.2540 | 18.0574 | 324 |
| preliminary-main-1 | shared only | 0 | 639.10 | 24.2613 | 36.8432 | 3211 |
| preliminary-main-1 | shared only | 1 | 636.79 | 24.5687 | 36.1649 | 3200 |
| preliminary-main-1 | shared only | 2 | 624.33 | 25.0417 | 35.1285 | 3136 |
| preliminary-main-1 | shared only | 3 | 624.60 | 25.0116 | 32.9897 | 3137 |
| preliminary-main-1 | shared only | 4 | 627.72 | 25.1418 | 33.5807 | 3153 |
| preliminary-main-1 | shared only | 5 | 639.85 | 24.5768 | 31.0545 | 3214 |
| preliminary-main-1 | shared+4 | 0 | 307.01 | 51.6272 | 63.8070 | 1549 |
| preliminary-main-1 | shared+4 | 1 | 301.37 | 52.4398 | 62.3250 | 1523 |
| preliminary-main-1 | shared+4 | 2 | 303.11 | 51.8321 | 66.3278 | 1530 |
| preliminary-main-1 | shared+4 | 3 | 302.72 | 52.4950 | 61.4544 | 1528 |
| preliminary-main-1 | shared+4 | 4 | 307.22 | 51.7693 | 58.4876 | 1550 |
| preliminary-main-1 | shared+4 | 5 | 306.51 | 51.7552 | 65.9494 | 1549 |
| preliminary-main-1 | disjoint 5 | 0 | 1336.34 | 11.5812 | 22.8095 | 6693 |
| preliminary-main-1 | disjoint 5 | 1 | 1325.98 | 11.7114 | 21.2156 | 6642 |
| preliminary-main-1 | disjoint 5 | 2 | 1244.33 | 12.5572 | 23.1892 | 6236 |
| preliminary-main-1 | disjoint 5 | 3 | 1158.87 | 13.2831 | 26.0725 | 5806 |
| preliminary-main-1 | disjoint 5 | 4 | 1173.56 | 13.2065 | 24.0874 | 5879 |
| preliminary-main-1 | disjoint 5 | 5 | 1166.79 | 13.2868 | 23.7087 | 5848 |

The final restored branch rebuild passed on both TFMs (365 warnings, 0 errors); the restored statement-count test passed once per TFM with the same counts above. Verification is recorded in rebuild log (local evidence, not committed) and count log (local evidence, not committed). `git diff --check` passes. All final source bytes match the frozen benchmark backup.

---

## Historical attempts (frozen reports)

The following second-attempt report and its embedded first-attempt table are retained unchanged. Statements about all-key arrays and failed gates describe those earlier implementations, not packet rev 4.

# SEK-G162 follow-up verification — AC4 FAIL

Measured 2026-11-13 against `origin/main` / `e1478efefc578db03e68b06d66a1898270530db0`. The confirmed statistics-sensitive tag-history scans were replaced with bounded composite-index top-1 seeks. Correctness and constant statement-count checks pass on both TFMs. **Performance acceptance fails**; the complete numbers follow. Work stops here without further tuning.

The timing harness (`Program.cs`), connection settings, writer workloads, seeding, truncation, warm-up and measurement duration were unchanged. Two complete invocations per implementation ran sequentially in **main-1 → branch-1 → main-2 → branch-2** order. Every invocation has six cases, each with one warm-up plus five measured five-second runs. No builds/tests/profilers/SQL observers ran alongside acceptance timing; the separate diagnostic container was stopped first. Both main substitutions used only the store source, with exact branch bytes restored afterward. SHA-256 values and byte-for-byte restoration verification (local evidence, not committed); [reproduction](README.md).

Machine/runtime: Apple M4 Max (`Mac16,5`), arm64, 16 CPUs, 128 GB RAM, macOS 27.2.1, .NET SDK 11.0.100 / benchmark runtime 10.0.12, Npgsql 10.0.3, Docker PostgreSQL 16.15 (`postgres:16-alpine`). Both test TFMs are net9.0 and net10.0; timing uses net10.0. No benchmark parameter or server setting was changed to improve results.

## Diagnosis before changing SQL

The generic-plan-after-five-executions hypothesis is **not supported**. The benchmark uses `new Factory(postgres.GetConnectionString())` and `UseNpgsql(connection)` without a data-source override, `Max Auto Prepare`, or `Prepare()`. The installed Npgsql 9/10 documentation states that `MaxAutoPrepare=0` by default (disabled); the default `AutoPrepareMinUsages=5` does not enable it. The observed server commands execute as `<unnamed>`, not named prepared statements. Forced custom/generic plans below are diagnostic probes, not acceptance settings.

Before editing production SQL, all seven raw seam statement shapes and main's corresponding per-key statements were extracted and run by hand with `EXPLAIN (ANALYZE, BUFFERS)` under both forced plan modes, at 2,500–20,000 tag rows and 1–80 heads. The retained matrix was subsequently refreshed from the frozen first-attempt source, extending the disjoint fixture to 65,000 tag rows. It covers both missing column statistics and explicit `ANALYZE`; reconciliation also exercises null heads. See the complete plan matrix, fixture sizes and reproduction instructions (local evidence, not committed). An additional instrumented execution of the unchanged first-attempt branch reproduced high end-to-end latency; its raw diagnostic runs (local evidence, not committed), live reconciliation plans/statistics (local evidence, not committed), and compressed server log (local evidence, not committed) are retained. This diagnostic execution is separate from acceptance timing.

The strongest finding is **statistics-sensitive history scans**, not a generic-plan transition:

- **Phase 2 bootstrap:** the first attempt joins all matching tag history and groups it. At 20 tags / 20,000 rows without statistics, it estimates one row per key, actually reads 1,000 each, sorts 20,000 rows, and aggregates them. Execution: 9.510 ms custom / 9.878 ms generic. With statistics it can hash-join a full table scan instead. This affects new-head bootstrapping; timed benchmark heads are seeded, so it does **not** explain steady-state measured appends.
- **Phase 3 reconciliation:** without statistics, the `MAX`/`UNION ALL` shape can lose PostgreSQL's min/max top-1 optimization. At 20 tags, the index condition contains only service/tag; position becomes a filter over 1,000 historical rows per key. Execution: 1.502 ms custom / 1.461 ms generic in the fixture, versus 0.130 / 0.119 ms after the fix. The live benchmark independently showed the same plan in both modes, with about 4,000 buffer hits and 574 rows removed per key. Its `pg_stats` capture had no column-statistics rows at that moment. `ANALYZE` changes the fixture's first-attempt plan to backward composite-index scans; no benchmark `ANALYZE` was added.
- **Main:** per-key `MAX` can also choose history scans without statistics (20-tag fixture: one key removes 1,000 rows, 19 buffer hits, 0.096 / 0.099 ms custom/generic). A generic optional-head `OR` can keep the position comparison as a filter even at smaller sizes. Main is not intrinsically immune. The branch additionally plans an append node, two aggregate subplans and a null-head guard for every reconciliation. At one tag, lock-query planning is roughly 0.34–0.38 ms for the array join versus about 0.15–0.17 ms for main's direct lookup in these captures. These isolated timings support extra planning work; they do not establish that all of the application p50 difference is server planning.
- **Other seam statements:** lazy insert, locking, audit insert, cleanup and head advance have small fixed-head/array work in the matrix; they do not scan tag history. Under shared contention, live auto_explain attributes the long lock statement to `LockRows` waits, while its child lookup finishes in microseconds.

Relevant first-attempt excerpt (20 tags, no statistics, custom; generic has the same shape):

```text
Append ... loops=20
  Aggregate
    Index Scan using "IX_Tags_Service_Tag" on dcb_tags
      Index Cond: (("ServiceId" = k.service_id) AND ("Tag" = k.tag))
      Filter: ("SortableUniqueId" > k.position)
      Rows Removed by Filter: 1000
Execution Time: 1.502 ms
```

There is a remaining limit to the diagnosis: the diagnostic run reproduced approximately 50 ms application p99s, but logged serial server executions were only around 2–3 ms (including a 3.257 ms reconciliation execution). The history scans explain growing server work and a plan cliff, but **do not fully establish the cause of the end-to-end tail spikes**. Runtime/client scheduling or pauses remain unproven possibilities. The diagnostic observer itself adds overhead; those runs are not used for acceptance.

## SQL change and verification

A plain equality lookup with `ORDER BY SortableUniqueId DESC LIMIT 1` was tested first. Without statistics it still chose `Index Scan → Sort → Limit`, reading all 1,000 rows per tag. A two-sided key-range experiment also admitted an incremental-sort plan. Those rejected SQL experiments remain in the plan directory; neither was used for acceptance timing.

The final bootstrap and reconciliation statements use a **predecessor seek** in the existing `(ServiceId, Tag, SortableUniqueId)` index:

```sql
CROSS JOIN LATERAL (
  SELECT t."ServiceId" AS service_id, t."Tag" AS tag,
         t."SortableUniqueId" AS position
  FROM dcb_tags t
  WHERE (t."ServiceId", t."Tag") <= (k.service_id, k.tag)
  ORDER BY t."ServiceId" DESC, t."Tag" DESC, t."SortableUniqueId" DESC
  LIMIT 1
) m
-- Outside LIMIT: reject a predecessor belonging to a different key.
WHERE m.service_id = k.service_id AND m.tag = k.tag
```

The upper bound starts the backward scan at the requested key's end; full index ordering selects its newest position. If the key does not exist, the candidate belongs to an earlier key and the outer equality rejects it. Keeping that equality outside `LIMIT` also prevents low equality cardinality estimates from choosing a history scan/sort. Reconciliation then applies `(k.position IS NULL OR m.position > k.position)` outside `LIMIT`. It is still a separate statement/snapshot after all lock waits. Empty bootstrap keys retain null heads.

Final excerpt (20 tags, no statistics):

```text
Limit ... rows=1 loops=20
  Index Only Scan Backward using "IX_Tags_Service_Tag_SortableUniqueId"
    Index Cond: (ROW("ServiceId", "Tag") <= ROW(k.service_id, k.tag))
    Heap Fetches: 20
Buffers: shared hit=80
Execution Time: 0.130 ms custom / 0.119 ms generic
```

Both history lookups use this composite-index top-1 shape in **all 20 final plan files**: five fixtures × two statistics states × two plan modes. No history sort appears. Bootstrap improves to 0.338 / 0.311 ms custom/generic in the 20-tag fixture. Small-table head joins may still use a sequential scan of the tiny head table; that is distinct from reading growing tag history.

The phase boundaries and all six hooks, ordinal key/lock/insert/audit ordering, C# expectation checks, repair-only commit behavior, savepoint behavior, and EF event/tag DML remain intact. Arrays and the same statements are used for every tag count, including one tag. No stored procedure, production schema/index/migration change, or other-store change was made. The statement recorder now recognizes the raw top-1 SELECT rather than requiring the removed `MAX` token, while still excluding EF event/tag DML. The added missing-key test covers lazy and existing-null heads, same-service predecessors and an earlier-service predecessor, checking that no borrowed head or repair is persisted.

## Correctness and statement counts

All commands were completed before acceptance timing, from the repository root. Every test target ran on both net9.0 and net10.0.

| Command | net9.0 | net10.0 |
|---|---:|---:|
| `dotnet test dcb/tests/Sekiban.Dcb.Postgres.Tests -c Release -p:GenerateSBOM=false --no-build --no-restore --logger 'console;verbosity=detailed'` | 125 passed | 125 passed |
| `dotnet test dcb/tests/Sekiban.Dcb.WithResult.Tests -c Release -p:GenerateSBOM=false --no-build --no-restore --filter 'FullyQualifiedName~Postgres\|FullyQualifiedName~ConditionalAppend'` | 141 passed | 141 passed |
| `dotnet test dcb/tests/Sekiban.Dcb.Orleans.Tests -c Release -p:GenerateSBOM=false --no-build --no-restore --filter FullyQualifiedName~TwoClusterPostgresProductTests` | 12 passed | 12 passed |
| Main constant-count test, built against main before timing | 1 expected failure | 1 expected failure |

No failed/skipped branch tests. Full solution rebuild: `dotnet build dcb/Sekiban.Dcb.slnx -c Release -t:Rebuild -p:GenerateSBOM=false --no-restore` passed for both TFMs, 365 warnings / 0 errors, matching the first attempt's warning count. PostgreSQL test log (local evidence, not committed), WithResult (local evidence, not committed), Orleans (local evidence, not committed), main count probe (local evidence, not committed).

| Scenario | Implementation | 1 tag | 5 tags | 20 tags |
|---|---|---:|---:|---:|
| Success | main | 4 | 20 | 80 |
| Success | branch | 4 | 4 | 4 |
| Repair-only conflict | main | 10 | 50 | 200 |
| Repair-only conflict | branch | 7 | 7 | 7 |

Counts agree on both TFMs. Main intentionally fails the constant-count assertion after printing all six counts. Branch's count test is included in the passing full suite. The added regression test is `MissingKeys_DoNotBorrowPredecessorHeadsOrRepairs`; all previous suites, including direct ordinal blocking probes, remain included.

After all four timing invocations, the restored branch was rebuilt across both TFMs again (365 warnings / 0 errors), and the statement-count test was rerun on both TFMs: 1 passed each, with success 4/4/4 and repair-only 7/7/7. Final rebuild log (local evidence, not committed), final count log (local evidence, not committed). The final source SHA-256 matches the saved branch bytes; all four raw files contain the expected 36 runs and six summaries, and each summary was checked against its measured runs. `git diff --check` passes.

## Alternating benchmark: every measured throughput

Each cell lists runs 1–5 in execution order, in successful appends/s; rounding is for display only. Ratios use the unrounded median of each five-run block. Thresholds remain ≥95% for one tag/shared-only and ≥110% for five tags/20 tags/shared+4. Disjoint is reported without a threshold. Both blocks are retained and evaluated separately; runs were not pooled or selected to rescue a gate.

| Case | Block | Main runs 1–5 (appends/s) | Branch runs 1–5 (appends/s) | Main median | Branch median | Branch/main | Gate |
|---|---:|---|---|---:|---:|---:|---|
| 1 tag | 1 | 481.84, 473.64, 473.23, 458.93, 476.47 | 427.71, 434.29, 441.16, 433.70, 443.28 | 473.64 | 434.29 | 91.7% | FAIL |
| 1 tag | 2 | 480.76, 488.84, 488.52, 467.48, 476.10 | 443.65, 444.17, 448.49, 446.41, 448.32 | 480.76 | 446.41 | 92.9% | FAIL |
| 5 tags | 1 | 185.44, 183.99, 191.05, 187.49, 188.56 | 224.46, 153.58, 96.97, 257.30, 227.43 | 187.49 | 224.46 | 119.7% | PASS |
| 5 tags | 2 | 192.74, 191.69, 193.64, 189.14, 191.63 | 403.18, 388.15, 386.58, 396.52, 394.43 | 191.69 | 394.43 | 205.8% | PASS |
| 20 tags | 1 | 58.57, 57.10, 58.49, 58.68, 58.20 | 75.13, 96.80, 100.75, 70.72, 87.19 | 58.49 | 87.19 | 149.1% | PASS |
| 20 tags | 2 | 58.62, 58.51, 59.45, 58.08, 59.04 | 102.35, 109.58, 112.91, 109.97, 112.53 | 58.62 | 109.97 | 187.6% | PASS |
| shared only | 1 | 560.26, 558.57, 558.55, 571.15, 567.74 | 365.15, 247.66, 352.16, 414.36, 484.97 | 560.26 | 365.15 | 65.2% | FAIL |
| shared only | 2 | 572.34, 570.81, 579.23, 557.25, 573.32 | 521.41, 541.37, 532.27, 546.35, 541.27 | 572.34 | 541.27 | 94.6% | FAIL |
| shared+4 | 1 | 273.21, 268.75, 275.04, 272.23, 274.48 | 463.09, 481.93, 483.71, 482.98, 465.35 | 273.21 | 481.93 | 176.4% | PASS |
| shared+4 | 2 | 274.73, 275.07, 275.44, 268.84, 269.05 | 484.94, 483.85, 485.06, 319.67, 339.91 | 274.73 | 483.85 | 176.1% | PASS |
| disjoint 5 | 1 | 1235.26, 1245.90, 1238.90, 1247.14, 1243.13 | 2237.68, 2219.08, 2247.22, 2215.98, 2251.72 | 1243.13 | 2237.68 | 180.0% | reported |
| disjoint 5 | 2 | 1206.13, 1198.38, 1191.33, 1188.37, 1202.95 | 2675.14, 2690.37, 2596.63, 2574.16, 2663.67 | 1198.38 | 2663.67 | 222.3% | reported |

## Latency and successful writes

p50/p99 below are medians of the five per-run percentiles in milliseconds; successful writes sum all five measured runs, excluding seeds/warm-up. Every individual latency percentile and successful count, including all warm-ups, is in the four raw files: main block 1 (local evidence, not committed), branch block 1 (local evidence, not committed), main block 2 (local evidence, not committed), branch block 2 (local evidence, not committed). All appends succeeded; any failed write would have terminated the harness.

| Case | Block | Main p50/p99 ms | Branch p50/p99 ms | Successful writes main/branch |
|---|---:|---:|---:|---:|
| 1 tag | 1 | 2.046/2.965 | 2.218/3.313 | 11824/10904 |
| 1 tag | 2 | 2.011/2.879 | 2.185/3.046 | 12012/11158 |
| 5 tags | 1 | 5.206/7.498 | 3.335/46.270 | 4685/4812 |
| 5 tags | 2 | 5.109/6.547 | 2.476/3.505 | 4798/9846 |
| 20 tags | 1 | 16.919/20.638 | 5.894/97.056 | 1457/2155 |
| 20 tags | 2 | 16.846/20.875 | 5.114/58.229 | 1472/2739 |
| shared only | 1 | 27.984/37.243 | 42.790/77.715 | 14158/9389 |
| shared only | 2 | 27.460/37.308 | 28.994/42.448 | 14342/13484 |
| shared+4 | 1 | 57.728/72.067 | 32.522/45.282 | 6893/11960 |
| shared+4 | 2 | 57.727/71.670 | 32.516/45.216 | 6889/10631 |
| disjoint 5 | 1 | 12.402/23.410 | 6.603/13.859 | 31113/55922 |
| disjoint 5 | 2 | 12.911/23.756 | 5.609/11.752 | 30000/66067 |

The SQL plan regression is addressed in the captured custom/generic matrices, but this does not establish an end-to-end performance fix. The acceptance measurements retain instability and tail latency. Remaining fixed array/join/planning cost and the unexplained application latency prevent claiming the performance objective is achieved. No single-tag path or benchmark tuning was introduced. AC4 remains failed; no CI/publication was triggered.

## First attempt — original failed table

The first attempt's table is preserved below, unchanged. Its original report and raw runs are retained in [FIRST_ATTEMPT.md](FIRST_ATTEMPT.md), original main (local evidence, not committed), and original branch (local evidence, not committed).

| Case | Main appends/s | Branch appends/s | Branch/main | Gate | Main p50/p99 ms | Branch p50/p99 ms | Successful writes main/branch |
|---|---:|---:|---:|---|---:|---:|---:|
| 1 tag | 477.77 | 374.63 | 78.4% | FAIL (≥95%) | 2.045/2.922 | 2.562/4.206 | 11890/9114 |
| 5 tags | 189.20 | 206.23 | 109.0% | FAIL (≥110%) | 5.198/7.151 | 3.167/55.988 | 4740/5191 |
| 20 tags | 58.14 | 150.53 | 258.9% | PASS (≥110%) | 16.913/21.431 | 4.860/48.348 | 1454/3687 |
| shared only | 569.40 | 489.85 | 86.0% | FAIL (≥95%) | 27.541/38.714 | 32.224/45.220 | 14343/12388 |
| shared+4 | 276.12 | 443.40 | 160.6% | PASS (≥110%) | 57.290/73.428 | 35.273/50.627 | 6866/11027 |
| disjoint 5 | 1198.61 | 2484.25 | 207.3% | reported | 12.854/24.485 | 5.910/14.015 | 30069/61353 |
