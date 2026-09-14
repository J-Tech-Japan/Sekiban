# SEK-G79 PR #1235 final exact-head review

- Review date: 2026-09-13 (America/Los_Angeles)
- Repository: `J-Tech-Japan/Sekiban`
- PR: #1235
- Contract: standalone Issue #1234
- Reviewed head: `01b3843276fa3bdd828afd484eb2fa0e8a6b63bb`
- Base at review: `bfb43b98472f184257016868884e65889ccce587`
- Checkout: isolated clean detached checkout
- Verdict: **APPROVE**

## Contract and scope

I re-derived AC1–AC10 from Issue #1234 and did not fill gaps from host-only metadata. The review guide resolved no supplementary packet or intent path for this execution unit, so Issue #1234 was the primary contract. I inspected the full PR diff and the repair delta from the preceding exact-head review.

The change remains within the release-integration tooling, validation, workflow, fixture, documentation, and template-version scope. The PR neither creates tags nor publishes packages/templates, creates a release, closes issues, or performs the post-publication host writeback. Its body retains `Closes #1234`, while actual closure remains gated by the two-phase release record and post-publication process required by the issue.

## Prior finding closure

### F1 — private canonical record and immutable/live-tag binding: closed

The workflows now use the actual private host `J-Tech-Japan/SekibanIntentHost`, a dedicated `SEKIBAN_RELEASE_RECORD_TOKEN`, and an immutable 40-hex `SEKIBAN_RELEASE_RECORD_REF`. The reader binds the retrieved record bytes to all of the following:

1. the exact requested host commit response;
2. that commit's recursive tree entry at the canonical release-record path;
3. the Contents API blob SHA; and
4. a locally recomputed Git blob SHA over the downloaded bytes.

The library gate reads only `library-tagged` or `incomplete` state and validates the exact remote tag object plus its peeled commit. The template gate reads only `libraries-verified`, then checks the live template tag object's identity against the locally fetched tag object and verifies both peeled commits against the recorded merged commit. Missing credentials, mutable refs, wrong repositories, commit/tree/blob mismatches, tag-object substitution, peeled-SHA substitution, and invalid state transitions fail closed.

Killing fixtures exercise the positive immutable commit/tree/blob/tag path and reject each of those mutations. Workflow-source fixtures also reject fallback to `github.token` and the former host repository.

### F2 — exact integrated-head inventory and durable diff evidence: closed

`prepared` now requires exactly six named entries:

- `dcbTestsNet9`
- `dcbTestsNet10`
- `packagedConsumer`
- `templateConsumer`
- `SonarCloud Code Analysis`
- `diff`

The `diff` entry is not a narrative assertion: it requires command `git diff --check`, a passed result, the exact integrated head, post-merge timing, a positive attempt number, `superseded: false`, and the SHA-256 of the durable empty output artifact (`e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855`). The independent exact-head review identity remains a distinct required object rather than being conflated with CI.

Fixtures reject missing, renamed, duplicate, stale, failed, wrong-head, wrong-command, wrong-digest, and otherwise noncanonical entries.

### F3 — one monotonic budget at real CLI boundaries: closed

Both library-package and template visibility loops derive one deadline from a monotonic clock and recompute remaining time immediately before every real CLI/network request and retry delay. Connect timeout and maximum request time are capped by the remaining overall budget, curl's independent retry mechanism is disabled, and retry sleeps are also capped. Budget exhaustion reports the exact still-pending IDs, including the current item and all unvisited items, instead of silently resetting a per-request timeout.

The actual CLI-boundary self-tests cover delayed success, a deliberately slow endpoint, malformed responses, overall deadline enforcement, and exact first/last pending diagnostics for both library and template paths.

## Private-host probe evidence ruling

The credentialed probe against the real private `J-Tech-Japan/SekibanIntentHost` was **not run** at this head because neither the dedicated secret nor an immutable record ref was supplied to the PR run. I do not represent that probe as executed.

This is not a PR-approval blocker. Issue #1234 makes the PR release-free and requires deterministic read-only fixtures for the private-host boundary; this head provides those fixtures, exercises the production reader through a faithful `gh api` shim, and makes missing credential/ref input fail closed. Requiring the reviewer to configure a repository secret or manufacture a host record would cross the authorized PR boundary and would not prove a real future release state.

The minimal safe operator action is nevertheless mandatory before either release tag is created: provision the least-privilege read-only `SEKIBAN_RELEASE_RECORD_TOKEN`, set `SEKIBAN_RELEASE_RECORD_REF` to the exact immutable host commit containing the intended canonical record state, and run the production reader smoke test against the private host. If commit/tree/blob binding, state, tag-object identity, or peeled-SHA verification fails, stop before tagging or publication. The release record must preserve that evidence; this approval is not a waiver of that operational gate.

## AC1–AC10 verification

- **AC1 / version and manifest:** the canonical manifest remains exactly 26 packages at `10.22.0`; dependency/version validation and source-mapped isolated local-feed consumption remain mandatory.
- **AC2 / templates:** all five template packages remain pinned to `10.22.0`, with Orleans `10.3.1` alignment and isolated consumer proof.
- **AC3 / integrated-head evidence:** the exact six-entry inventory, independent review identity, canonical UTC chronology, exact head, attempt/supersession rules, and durable `git diff --check` artifact are enforced.
- **AC4 / release bodies:** checked-in library and template bodies remain non-empty, bilingual, digest-bound, and factually inventoried.
- **AC5 / state machine:** the six states and allowed transitions remain explicit; repository root, four body digests, PR/run/job/review/release/package identities, and immutable record binding are compulsory where applicable.
- **AC6 / library-before-template order:** library verification is immutable before template tagging; workflows consume the correct pre-tag record states and validate exact live tag objects and peeled commits.
- **AC7 / finite public visibility:** both package and template paths use a single monotonic overall deadline with bounded requests, bounded sleeps, retries, and exact pending diagnostics.
- **AC8 / recovery and idempotency:** same-source/reproducible or signed-copy semantic package equivalence is accepted, changed payload at the same version is rejected, and template retry is idempotent without advancing an invalid state.
- **AC9 / release-free PR and closeout:** PR validation is publication-free; issue closure and host knowledge writeback remain post-publication actions after artifact verification.
- **AC10 / negative coverage:** deterministic fixtures kill mutable refs, wrong host/token use, commit/tree/blob/tag substitutions, malformed or incomplete state, inventory mutations, deadline mutations, and semantic package-content changes.

## Verification evidence

Local checks in the clean exact-head checkout:

- `git diff --check`: passed.
- Shell syntax validation for changed release scripts: passed.
- `TemplateValidation` Release build: passed with zero warnings and zero errors.
- workflow validation: passed.
- complete-record validation: passed.
- release-gate self-test suite: passed, including manifest/dependency parity, host-reader immutable-binding fixtures, record-state fixtures, semantic package retry/equivalence tests, and both real CLI deadline paths.

Live GitHub evidence at exact head `01b3843276fa3bdd828afd484eb2fa0e8a6b63bb`:

- DCB tests .NET 9: success.
- DCB tests .NET 10: success.
- Azure Queue packaged-consumer validation: success.
- template packaged-consumer validation: success.
- SonarCloud Code Analysis: success.
- SonarCloud quality check: success.
- scheduled-only currency job: conditional skip on the PR event.
- Pending: 0; failures: 0.

The exact CI logs show the host-reader shim exercising credential/ref/commit/tree/blob/tag-object/peeled-SHA/state checks; packaged consumer validation; semantic-equivalence acceptance and changed-content rejection; and the expected explicit message that the optional real credentialed private-host probe was not run.

Same-account GitHub review evidence: COMMENTED review `5189565347`, submitted at `2026-09-13T04:46:14Z` against commit `01b3843276fa3bdd828afd484eb2fa0e8a6b63bb`: https://github.com/J-Tech-Japan/Sekiban/pull/1235#pullrequestreview-5189565347

## Findings

No material implementation finding or required PR-evidence gap remains at the reviewed exact head.

## Verdict

**APPROVE** for `01b3843276fa3bdd828afd484eb2fa0e8a6b63bb` only. Any head change requires a fresh review and fresh exact-head CI assessment. The unexecuted private-host probe remains a mandatory pre-tag operator gate as stated above, not evidence claimed by this review.
