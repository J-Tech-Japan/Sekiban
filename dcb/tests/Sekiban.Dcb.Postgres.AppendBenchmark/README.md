# PostgreSQL append benchmark (manual)

This console project is built by `Sekiban.Dcb.slnx` but is not a test project and is not included in `run_test_dcb.yml`. Docker must be running. No production dependency is added.

It measures the ordinary typed append API, `PostgresEventStore.WriteEventsAsync` (`TagHeadWriter.TypedBatch`), with no expectations. All writes, including shared-tag writes, must succeed; a failure terminates the benchmark. Successful writes alone contribute to throughput and latency.

Each case gets a fresh set of tables, seeded heads outside timing, one warm-up run, then five measured runs. Each run lasts five seconds by default; in-flight appends complete before elapsed time is recorded. The 1/5/20-tag cases use one writer and reuse a fixed tag set. Shared-only uses 16 writers appending only the shared tag. Shared+4 uses 16 writers with the shared tag and four private tags per writer. Disjoint uses 16 writers with five private tags each. Positions increase process-wide; the ordinary writer intentionally imposes no expectations. The durable-head max rule still applies when concurrent events arrive out of position order.

The summary reports median throughput across five runs and medians of the five per-run p50/p99 latencies, in milliseconds. It also prints every measured successful-write count and each individual run as JSON. Warm-up results are explicitly marked and excluded from summaries. Avoid concurrent test/build workloads during timing.

From the repository root:

```sh
dotnet build dcb/tests/Sekiban.Dcb.Postgres.AppendBenchmark -c Release -f net10.0 -p:GenerateSBOM=false
dotnet run --project dcb/tests/Sekiban.Dcb.Postgres.AppendBenchmark -c Release -f net10.0 --no-build -- branch 5 > /tmp/g162-benchmark-branch.log
```

Use exactly the same benchmark for main. Save the branch source to a temporary file, replace only the store source, build/run, then restore it even if a command fails. Do not stash or reset other work:

```sh
store=dcb/src/Sekiban.Dcb.Postgres/PostgresEventStore.cs
backup=$(mktemp /tmp/g162-store.XXXXXX)
cp "$store" "$backup"
trap 'cp "$backup" "$store"' EXIT INT TERM
git show origin/main:dcb/src/Sekiban.Dcb.Postgres/PostgresEventStore.cs > "$store"
dotnet build dcb/tests/Sekiban.Dcb.Postgres.AppendBenchmark -c Release -f net10.0 -p:GenerateSBOM=false
dotnet run --project dcb/tests/Sekiban.Dcb.Postgres.AppendBenchmark -c Release -f net10.0 --no-build -- main 5 > /tmp/g162-benchmark-main.log
# This new assertion is EXPECTED to fail on main after printing all counts.
dotnet test dcb/tests/Sekiban.Dcb.Postgres.Tests -c Release -f net10.0 -p:GenerateSBOM=false --filter FullyQualifiedName~StatementCounts_AreConstant --logger 'console;verbosity=detailed'
cp "$backup" "$store"
trap - EXIT INT TERM
git diff -- "$store"
# Rebuild the restored branch before further tests/runs.
```

Acceptance compares branch/main median throughput: at least 95% for 1 tag and shared-only; at least 110% for 5 tags, 20 tags and shared+4. Disjoint is reported without a threshold. A failed gate must be reported without changing the benchmark to make it pass.

The statement-count test uses the Npgsql ActivitySource and a parent TraceId, including both Npgsql 9 and 10 SQL tag names. The compared scenarios have three shapes at every size (1, 2, 5 and 20): existing heads for success; repaired existing heads for a true repair-only conflict; and the historical repair+cleanup shape with an equally sized set of unrelated lazy heads. That last shape has twice as many distinct keys as its size label. The one-key path must match main (4 success statements, 5 repair-only statements); multi-key counts must be constant. Empty-subset SQL is skipped. EF event/tag commands and epoch checks are excluded.

Local main-versus-branch evidence, diagnosis, alternating reruns, statement counts and test commands: [RESULTS.md](RESULTS.md). The original failed report is preserved in [FIRST_ATTEMPT.md](FIRST_ATTEMPT.md).

Each alternating verification uses two complete blocks per implementation in `main → branch → main → branch` order. The timing harness in `Program.cs` is unchanged. Each invocation still uses one warm-up and five measured five-second runs per case; no observer, profiler, SQL settings override, build or test runs alongside acceptance timing. Every invocation creates its own PostgreSQL container. Exact reproduction (after the correctness suites):

```sh
python3 dcb/tests/Sekiban.Dcb.Postgres.AppendBenchmark/run_alternating.py
```

Rev 4 records `results-rev4-{main,branch}-r{1,2}-net10.jsonl` and `alternating-rev4-verification.json` (earlier files are retained), with a temporary-file source backup, source SHA-256 values and byte-for-byte restoration checks after each main run. It also repeats the expected-to-fail main statement-count test on both TFMs before timing. Diagnostic SQL capture is separate; see plans/README.md (local evidence, not committed).

Packet rev 4 selects main’s exact per-key SQL for a batch with exactly one distinct key; batches with two or more keys use the bounded-seek set-based SQL. The harness and timing method are unchanged. A gate passes only when its threshold passes separately in both blocks. Do not pool runs across blocks.

## Rev 5 additional verification

Main means the exact `PostgresEventStore.cs` bytes at `origin/main` commit
`e1478efefc578db03e68b06d66a1898270530db0` (`e1478efe`). Main is obtained by a
**temporary file swap of only that source file**, built with the same harness.
A temporary-file backup is restored **byte-for-byte in finally**, including on
failure; SHA-256 evidence is saved. No worktree reset/stash is used.

The rev 4 main-1, branch-1, main-2, branch-2 data is retained unchanged. Run the
additional control only after all tests/builds finish, with no concurrent workload:

```sh
python3 dcb/tests/Sekiban.Dcb.Postgres.AppendBenchmark/run_correctness.py
python3 dcb/tests/Sekiban.Dcb.Postgres.AppendBenchmark/run_control.py
# Rebuild restored branch after the control's main build.
dotnet build dcb/Sekiban.Dcb.slnx -c Release -p:GenerateSBOM=false --no-restore
```

`run_control.py` runs main twice back to back, with the unchanged harness: six
cases, one warm-up plus five measured five-second runs each. Both complete raw
outputs and restoration evidence are retained. RESULTS.md reports all throughput
runs, median ratios/spread, latency and successful-write counts.

`capture_main_sql.py` performs the same temporary swap and runs
`OneKey_RecordedTextsAndParameterNames_EqualMain` to generate the embedded
`../Sekiban.Dcb.Postgres.Tests/MainSql/*.json` fixtures. Regular tests compare
recorded SQL text and named placeholders with those fixtures. The main capture
includes lazy, bootstrap, existing and repaired one-key shapes; no production
recorder or instrumentation was added. Do not set `G162_MAIN_SQL_CAPTURE` for
normal correctness runs.

Long-history plans (two requested tags, **10,000 history rows per tag**):

```sh
docker run -d --rm --name g162-plans -e POSTGRES_PASSWORD=diagnostic postgres:16-alpine
# After pg_isready succeeds:
python3 dcb/tests/Sekiban.Dcb.Postgres.AppendBenchmark/capture_long_history.py
docker stop g162-plans
```

This extracts the exact set-based bootstrap and reconciliation SQL from the store,
uses the existing composite index, and captures `EXPLAIN (ANALYZE, BUFFERS)` for
fresh/analyzed statistics and custom/generic plans in `plans/rev5-long-history-*`.
It operates only on the disposable `g162-plans` container, outside benchmark timing.

The scripts write raw run output (`results-*.jsonl`) and build/test logs (`validation/`) next to this README; these are ignored by git. RESULTS.md records the summarized numbers from the runs used for the PR.
