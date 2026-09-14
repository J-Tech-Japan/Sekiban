# SEK-G80 / PR #1237 exact-head re-review

- Repository: `J-Tech-Japan/Sekiban`
- PR: #1237 (branch `codex/sek-g80-release-provenance-v2`; closing reference `Closes #1236` matches the source issue)
- Execution unit: SEK-G80 (child issue #1236)
- Intent task: `sek-g80-pr1237-aefca245-exact-review-20260914`
- Result nonce: `sek-g80-pr1237-review-aefca245`
- Reviewed head: `aefca245bdd4010b537657793eaa041c9c83d9c8`
- Prior reviewed heads: `592518181a55` (GitHub review 5196661930, REQUEST-UPDATE F7). Review tasks for `09545234` and `f26fd712` were disposed as superseded, with nothing posted.
- Base: `7f684e6b9f769d436b12495acd07e7d74c5d8298`
- Contract: standalone Issue #1236 (AC1-AC11), SEK-G80 packet, and approved design `intents/sekiban/design/dcb-10-22-release-sha-provenance-repair-2026-09-12.md` (host commit `f3e8d9c0`), with the "2026-09-14 design amendment: SHA-derived prerelease version" (host commit `7c51fe1`, on `origin/main`)
- Verdict: **APPROVE**

## Executive result

No material finding remains at this head. I checked the two commits since the last posted review against code, local runs, mutant experiments, and exact-head CI read through the GitHub API.

- **`09545234` (F7).** The receipt/delivery equality is replaced by the order `review submitted < record created_at <= receipt reported_at <= delivered_at < merge`.
  - Positive: a real-format fixture built from byte-exact host transport lines passes.
  - Restoring the old equality makes that positive fail at `implementation-review.completion.transport`.
  - Removing the ordering guard lets the `receipt-before-record` and `receipt-after-delivery` mutants pass; removing the merge bound lets `delivered-after-merge` pass.
- **`f26fd712` + `aefca245` (design amendment).**
  - The PostgreSQL harness now derives `10.0.2-g62.g<sha12>`. That identifier is letter-prefixed, so it is valid SemVer for every commit SHA.
  - The derivation check is now SDK-independent (F8) and pins the version actually passed to the first pack (A6).
  - The template validation workflow now also triggers on the PostgreSQL harness (A5).
  - The fresh API-proven PostgreSQL `workflow_dispatch` at this exact head succeeded.

Tests passing is treated as necessary, not sufficient. The approval rests on the packet/design conformance evidence below.

## Findings

None blocking.

### Closure of the last round's review-side findings

- **F8 (derivation check depended on SDK-specific restore text): closed.** The check now uses a differential on one probe project and one feed, varying only the requested version:
  - the letter-prefixed control must restore and resolve in `project.assets.json`;
  - the well-formed but absent `10.0.2-g62.592518181123` must fail with a NU110x resolution diagnostic;
  - the former `10.0.2-g62.095452346654` (and the synthetic `095452654654`) must fail restore with no NU110x and no resolved package, and print either "is not a valid version string" or MSB4181.

  Evidence:
  - CI template job `103968858050` prints `Version derivation probe SDK: 10.0.401` and both differential lines pass.
  - Locally on SDK 10.0.100 the same check exits 0.
  - At `f26fd712` this step had failed on 10.0.401 (MSB4181 only), so the repair addresses exactly the observed failure.
- **A6 (text-only version-line check): closed as far as a static check can reach.** The harness now has exactly one assignment each of `version` and `mutant_version`. `--print-candidate-version` prints those same assignments. A `dotnet` stub captures the first `-p:PackageVersion` of a real harness run for HEAD and requires it to equal the printed derivation.
  - The five harness mutants in CI were each rejected for their own reason (bare-prefix-derivation: NuGet rejection; bare-prefix-version-line: required line; reassigned-after-required-line and late-read-reassignment: assignment count; late-eval-reassignment: captured PackageVersion mismatch). I reproduced all five locally.
  - My extra mutants were also rejected: a `0%s` prefix (NuGet), a changed `-omission` suffix (required line), and an `export version=` reassignment (count).
- **A5 (path-trigger gap): closed.** `dcb_template_validation.yml` pull_request paths include `dcb/tests/Sekiban.Dcb.Postgres.Tests/run-packaged-consumer.sh`; that spelling matches the file in the tree. `Program.cs` workflow validation requires both that path and the check invocation. Both new workflow mutants fail in the CI log:
  - "The validation workflow must include 'dcb/tests/Sekiban.Dcb.Postgres.Tests/run-packaged-consumer.sh'."
  - "The packaged-consumer path must run the PostgreSQL SHA-derived version check against the real harness."

### Advisories (non-blocking)

- **A1.** Never re-run the #1235 origin runs (`34737699937`, `34738840878`, `34738842353`, `34738843321`); the pinned origin inventory would permanently fail.
- **A3.** The candidate `implementation_review` transport is authenticated by host-commit authority only; the prepared record must reference the real canonical lines.
- **A4.** No rule requires candidate `merged_at_utc` to follow the #1235 merge (`2026-09-13T04:47:20Z`). "Later PR" is enforced structurally: no #1235 commit or run may be reused, and the candidate must be a main-ancestor checkout that contains the v2 validator and the 10.22 bodies.
- **A6-residual.** A reassignment hidden from the static count (e.g. `eval`) placed after the first `pack_chain` call still passes the check; I reproduced this locally. It is self-detecting: the positive consumer would then restore a version absent from the feed and fail the real PostgreSQL run.
- **A7 (DCB test flake).** DCB Tests run `34841987339` attempt 1 net10 job `103968858457` failed in `MaterializedViewPostgresOrleansTests.Grain_Late_Create_Older_Than_CurrentPosition_Is_Applied_Without_Stalling_Other_Aggregates` (`Assert.False() Failure`, Expected False, Actual True). Attempt 2 at the same head passed (net10 `103973656590`, net9 `103973658601`). The same test failed identically in runs `34769767868` (this branch, `aac6cacc`), `34400373141` (G66) and `34273513315` (G57). This PR changes nothing under `dcb/src` or the MaterializedView tests. I accept the attempt-2 evidence for AC11 as a pre-existing flake; it should be tracked separately.
- **Pre-release operator gate.** The pinned origin reviewer transport lines (entry id `5a17253046a74745892392880124c244`) and review artifact `sek-g79-pr1235-01b38432-final-exact-codex-sol-review-20260913.md` are still absent from `J-Tech-Japan/SekibanIntentHost` `origin/main`. They must be committed and re-verified against the pinned digests before any prepared record or tag. The real credentialed private-host probe was not run and is not claimed.

## Amendment scope and regression check

- **Scope.** The diff against base adds exactly one file outside the original packet paths, `dcb/tests/Sekiban.Dcb.Postgres.Tests/run-packaged-consumer.sh`, which the amendment authorizes. In it:
  - only the version derivation, the `--print-candidate-version` mode, and the single-assignment restructuring changed;
  - pack, nuspec dependency assertions, the local candidate/mutant feeds, the omission mutant and the consumer runs are unchanged.

  All other delta files are TemplateValidation files and `dcb_template_validation.yml`. `git diff --check` is clean.
- **Audit of other SHA/number-derived NuGet versions: complete.**
  - The Azure Queue workflow uses `10.0.2-pr.${{ github.run_id }}`, which is valid because run IDs have no leading zero; the Orleans harness receives it via `--version`.
  - Template packing uses the fixed `10.22.0`, and the release workflows use tag-derived versions.
  - The `git rev-parse --short` uses in the AWS scripts are container image tags, not NuGet versions.
- **Validator unchanged.** `ClosedReleaseRecordValidator.cs`, `ReleaseBundle.cs`, the reader, the fixture generator, the mutator and the fixtures are unchanged since `09545234`.
  - At this head the local release-record harness section exits 0: `real-format-implementation-transport` passes, 191 named mutants are rejected at their asserted rules, and the name-set check passes (198 rejections in total).
  - The CI template job shows the same results plus "11 bundled test projects passed".

## Prior-finding closure matrix

| Finding | Result at `aefca245` | Evidence |
|---|---|---|
| F1 API-native origin/check evidence | Closed | Native fixtures and origin rules unchanged; matrix rows pass. |
| F2 two-parent main descendant | Closed | Unchanged. |
| F3 canonical origin completion | Closed | `origin.completion.canonical-bytes` pin unchanged; guard-removal proven at `59251818`. |
| F4 reachability anchors | Closed | Unchanged; `reader-reachable-splice` rejected at `graph.base`. |
| F5 discriminating mutations | Closed | 191-row table with name-set check. |
| F6 Sonar | Closed | Both Sonar checks green; 0 open code-scanning alerts. |
| F7 receipt/delivery equality | Closed | Ordering rule, real-format positive, three chronology mutants, guard removal. |
| F8 SDK-dependent derivation check | Closed | Differential control passes on SDK 10.0.401 (CI) and 10.0.100 (local). |
| A5 path trigger | Closed | Workflow path plus two workflow mutants. |
| A6 static version-line check | Closed (residual advisory) | Single-assignment count plus captured PackageVersion; 5 CI mutants and 3 extra local mutants rejected. |

## AC1-AC11 assessment

| AC | Result | Evidence |
|---|---|---|
| AC1 Closed v2 | Pass | `schema.members` and `envelope.schema-version` mutants. |
| AC2 Candidate provenance | Pass (A4) | Origin-only evidence, substitution mutants, candidate parents/trees/merge strategy/main ancestry. |
| AC3 Implementation Review | Pass (A3) | COMMENTED state with semantic APPROVE, body/artifact binding, completed transport with ordered instants, real-format positive. |
| AC4 Stage Reviews | Pass | `authority.*` rules, `authority-rebind`, state-prefix carries. |
| AC5 Acyclic history | Pass | `graph.*` rules, sibling/splice pair. |
| AC6 Immutable graph | Pass | Host anchors, reachability, `external-commit` versus `current-object-self-commit`. |
| AC7 Exact chronology | Pass | Candidate/prepared/tag/closure chronology; origin chronology; transport ordering. |
| AC8 Reader boundary | Pass | Reader interface and bundle mutants; credentialed probe not run (not supplied). |
| AC9 Event matrix | Pass | Currency check `schedule`-only (skipped on PR); packaged consumers on PR; PostgreSQL on dispatch. |
| AC10 Preserve G79 / scope | Pass | Scope as amended; package/nuspec/feed/inventory unchanged. |
| AC11 Release-free proof | Pass (A7) | CI table below; PostgreSQL `workflow_dispatch` API-proven at the exact head; no tag or release. |

## Verification evidence

- Isolated clone detached at `aefca245bdd4010b537657793eaa041c9c83d9c8`. TemplateValidation Release build: 0 warnings, 0 errors.
- Local runs:
  - `validate-candidate-version-derivation.sh` exits 0 on SDK 10.0.100; the stub-captured HEAD version is `10.0.2-g62.gaefca245bdd4`.
  - Release-record harness section: exit 0.
  - End-to-end check: the byte-exact host transport sample from commit `874e737` in the implementation-review slot, with a bracketing timeline, passes validation at this head.
- Exact-head CI:

| Check | Run | Job / check run | Head | Result |
|---|---|---|---|---|
| Run DCB Tests (attempt 1) | 34841987339 | net9 103968858443 / net10 103968858457 | aefca245 | success / failure (A7 flake) |
| Run DCB Tests (attempt 2) | 34841987339 | net10 103973656590 / net9 103973658601 | aefca245 | success / success |
| DCB Azure Queue packaged consumer | 34841987380 | 103968857885 | aefca245 | success |
| DCB template packaged consumer | 34841987425 | 103968858050 | aefca245 (PR merge ref) | success |
| Scheduled currency check (schedule-only) | 34841987425 | 103968859835 | aefca245 | skipped |
| PostgreSQL packaged consumer, pull_request | 34841987338 | 103968857825 | aefca245 | success |
| PostgreSQL packaged consumer, workflow_dispatch | 34842125920 | 103969300075 | aefca245 | success (version 10.0.2-g62.gaefca245bdd4) |
| SonarCloud Code Analysis | - | 103971109342 | aefca245 | success (Quality Gate passed) |
| SonarCloud | - | 103971111006 | aefca245 | success (analysis 1772227390, 0 results, 0 open alerts) |

- Release-free boundary: no `dcb-v10.22.0`/`dcbTemplates-v10.22.0` tag or GitHub Release exists.
- Out-of-scope boundaries in the issue body (publication, product APIs, package inventory, template pins, weakening G79 safeguards) were not crossed.

## Required disposition

APPROVE at exact head `aefca245bdd4010b537657793eaa041c9c83d9c8`. This approval does not authorize tagging, publishing or release. Those still require:

- the repair merge;
- freshly regenerated integrated-head evidence at the merge commit;
- the origin transport host-commit gate;
- the A1 constraint;
- the separate prepared-stage approval;
- the mandatory credentialed private-host probe.

Any new commit on the branch invalidates this approval.
