# SEK-G124 builder verification

No publication was performed. Real GitHub workflow execution is unavailable in this sandbox. Their first real run will be PR CI; merge remains contingent on the public 10.23.0 26/26 result and the two-runner result.

## Binding design items

1. Only root `_manifest/` is newly excluded by the shared canonical equality; nested paths remain compared.
2. Library public equality visits all 26 packages and collects every failure before returning failure.
3. The wait change is implemented at 3600 seconds, superseded by item 15; no shorter job timeout exists.
4. Retry fixtures contain different SPDX JSON and matching SHA-256 entries; SBOM-only passes, assembly and nuspec changes fail for both workflows.
5. All guards check the version being released; incomplete 10.23.0 history passes the 10.23.1 workflow fixture without changing guards.
6. Runbook documents re-packing and the sole new SBOM exclusion; the original 45-minute wording is superseded by item 15.
7. Five version authorities and template README are 10.23.1; Orleans remains 10.3.1.
8. Four 10.23.1 bodies copy the prior bodies with the version changed; both library bodies add the same incident explanation and recommendation.
9. Existing “From DCB 10.23.0” documentation stays unchanged.
10. No product code, tag, push, dispatch, package publication, GitHub Release or host record change.
11. Runbook records the after-merge steps 2–5 for the exact merged 10.23.1 commit; execution remains operator-owned.
12. New read-only released-library reproducibility workflow rebuilds the exact tag at the default workspace path with identical production commands; real 10.23.0 proof remains required in PR CI.
13. Two fresh CI jobs pack the PR head with production commands and exchange 26 packages via a workflow artifact; the second compares every package using shared equality.
14. Runbook requires reproducibility dispatch before retry, a new version on failure and prompt retry; tag workflows log SDK/image; pinning stays a candidate.
15. All three explicit waits are 3600 seconds and the harness rewrites the new literal to 10 seconds.
16. Runbook explains updating an existing Release, re-packed assets with different SBOM bytes, and symbol packages outside equality and Release assets.
17. Incident paragraph uses plain Markdown and the date “2026-10-06 (UTC)” in English and Japanese.
18. Fixtures distinguish drift before 10.23.1, between its tags and after both; next scheduled run is 2026-10-12 04:23 UTC.
19. Reproducibility triggers only on manual dispatch or PR changes to its own workflow file.
20. No exclusion or guard is relaxed on reproducibility failure; runbook requires stop/report and keeps 26/26 for this PR required before merge unless the operator decides otherwise.
21. SDK and runner-image diagnostics are logged; library equality separately reports missing locally, could not download and content differs.
22. Tags are fetched before resolving the version; the tag is built at workspace root before tooling is placed in RUNNER_TEMP; the new workflow uses the existing package-manifest check; nested manifest fixtures fail.
23. Runbook distinguishes same-time independent runners from SDK/image drift over time.
24. Runbook uses about 38 minutes from run start; push completed 15:01 UTC, all 26 visible by 15:37 UTC.

## Attempt and retry traces

These traces assume the exact tagged source, identical canonical payload, unchanged
triggering annotated tag object, prepared record and all current-version guards.
The harness simulates push and Release operations; it runs shell gates and equality
for real. It never publishes fixtures.

| Workflow / point | Attempt 1 | Retry after that failure | Reaches GitHub Release? |
| --- | --- | --- | --- |
| Library, successful path | record/body/manifest/package/consumer validation → live guard → duplicate-safe push → 3600-second visibility → all-26 equality → Release | Re-pack; validate; same tag object and absent current template tag; skip duplicates; visibility; equality; create/update Release | Yes on attempt 1; yes on valid retry |
| Library, visibility failure after push | Stops before equality and Release | Re-pack → retry guard → skip duplicates → visibility → equality → Release | No on failed attempt; yes on retry |
| Library, equality failure after push | Stops before Release | Same retry path; equality must now pass | No on failed attempt; yes if the failure was transient or SBOM-only; payload differences still block |
| Library, Release creation/update failure | Packages visible and equality passed; Release may be absent or partial | Same retry path; action creates or updates the Release and replaces its package assets | Yes, retry reaches create/update |
| Template, successful path | prepared record; same-version library tag/Release/body/assets/parity; library visibility → pack/consumer → same-version retry check (404 permits first publication) → live guard → push → template visibility → equality → Release | Re-pack; current library evidence and chronology; existing package semantic equality; same tag object; skip duplicates; visibility; equality; create/update Release | Yes on attempt 1; yes on valid retry |
| Template, visibility failure after push | Stops before equality and Release | Existing package retry check passes except excluded SBOM; live guard → skip duplicates → visibility → equality → Release | No on failed attempt; yes on retry |
| Template, equality failure after push | Stops before Release | Existing package retry check must pass; same tag object and library evidence; skip duplicates; visibility; equality; Release | No on failed attempt; yes if transient or SBOM-only; payload differences still block |
| Template, Release creation/update failure | Package visible and equality passed; Release may be absent or partial | Re-pack and all retry gates, then create/update Release | Yes, retry reaches create/update |

A library retry is intentionally blocked once the **current-version** template tag
exists. Changed tag objects, changed assembly/nuspec/other canonical payload, wrong
merged SHA, stale checks, changed release body/assets, or invalid chronology remain
failures. The injected recovery cases model a failure that clears; they do not prove
that a persistent payload mismatch can be retried successfully. Existing Release
updates are inferred from the [release action source](https://raw.githubusercontent.com/softprops/action-gh-release/v2/src/github.ts), not executed locally.

## Previous-tag completion assumptions

`check_library_live_guard`, `check_template_live_guard`, `check_live_tag`,
`check_publish_parity`, `check_library_release_evidence`, schema-3 prepared/complete
validation, and both literal tag workflow sequences reference the current version.
No guard requires the previous library tag to have a template tag or GitHub Release.
No publication guard was weakened or given a 10.23.0 exception.

The 10.23.1 fixture includes dcb-v10.23.0, with no matching template tag or Release
API endpoint. Both workflow sequences pass. Scheduled currency drift compares the
latest stable tags and correctly rejects library 10.23.0 / template 10.22.0 before
10.23.1, rejects library 10.23.1 / template 10.22.0 between tags, and passes after
both latest tags are 10.23.1. It does not audit completion of historical releases.

## Local verification

- Full workflow harness: 72/72 cases passed; consumer reader/record self-test: 40/40 cases passed. Every individual case/result is listed below.
- git diff --check passed. All changed paths are within the target paths; no dcb/src changes.

- Clean validator build with SDK 10.0.100: passed, zero warnings/errors.
- All four workflow files: valid YAML and actionlint structural validation; all shell blocks pass bash syntax checks. Full actionlint reports inherited unquoted-variable shellcheck warnings from the unchanged production commands; those commands remain exactly equal to the tag workflow.
- Reproducibility restore/build/validator-build/26-pack blocks are byte-identical to the library workflow commands; the existing package-manifest check passes for the new workflow.
- Exact workflow latest stable version resolver returns 10.23.0 with preview and invalid leading-zero tags present.
- read-template-version.sh prints 10.23.1.
- Both local consumer feeds packed 26 packages at 10.23.1; local determinism is 26/26 equal and its changed-content near case fails as required. These existing local packs disable SBOM and symbols and reuse build output; they are not the independent production CI proof.
- Full packaged-consumer sequence at 10.23.1 passed: all five generated template variants, restores/builds/tests, kept killing fixtures, reader/record self-test, release-tag self-test, and current/legacy status composition.
- Eight 10.22.0/10.23.0 body files remain byte-identical to origin/main; new bodies contain no prohibited placeholder letter sequences.
- Initial template consumer run exposed an existing fixture that treated 10.23.10 as malformed. The validator correctly rejected it as a wrong valid version. Only the fixture expected reason was corrected, retaining required rejection.

## Full workflow harness cases

- PASS reader recursive listing truncated but path walk succeeds 10.22.0
- PASS validator prepared 10.22.0 (older main ancestor; successful rerun attempt 2)
- PASS validator complete 10.22.0
- PASS BOTH workflow guard sequences 10.22.0: real schema-3 outputs; pack -> local validation -> simulated push -> visibility -> equality -> simulated Release
- PASS reader recursive listing truncated but path walk succeeds 42.7.3
- PASS validator prepared 42.7.3 (older main ancestor; successful rerun attempt 2)
- PASS validator complete 42.7.3
- PASS BOTH workflow guard sequences 42.7.3: real schema-3 outputs; pack -> local validation -> simulated push -> visibility -> equality -> simulated Release
- PASS incomplete 10.23.0 history drift before: rejects as required
- PASS incomplete 10.23.0 history drift between: rejects as required
- PASS incomplete 10.23.0 history drift after: equal at 10.23.1
- PASS reader recursive listing truncated but path walk succeeds 10.23.1
- PASS validator prepared 10.23.1 (older main ancestor; successful rerun attempt 2)
- PASS validator complete 10.23.1
- PASS BOTH workflow guard sequences 10.23.1: real schema-3 outputs; pack -> local validation -> simulated push -> visibility -> equality -> simulated Release
- PASS BOTH workflows retry with same triggering tag object
- PASS library retry SBOM-only difference reaches Release
- PASS library retry rejects assembly (root SBOM also differs)
- PASS library retry rejects nuspec (root SBOM also differs)
- PASS library retry rejects nested-manifest (root SBOM also differs)
- PASS library retry after failure at Wait for exact public library visibility: reaches Release
- PASS library retry after failure at Prove public library packages match local pack: reaches Release
- PASS library retry after failure at Create GitHub Release: reaches Release
- PASS template retry SBOM-only difference reaches Release
- PASS template retry rejects assembly (root SBOM also differs)
- PASS template retry rejects nuspec (root SBOM also differs)
- PASS template retry rejects nested-manifest (root SBOM also differs)
- PASS template retry after failure at Wait for exact public template visibility: reaches Release
- PASS template retry after failure at Prove public template package matches local pack: reaches Release
- PASS template retry after failure at Create GitHub Release: reaches Release
- PASS library equality aggregates missing locally, could not download, content differs
- PASS rejects wrong-merged-sha: merged_sha does not equal
- PASS rejects merged-sha-not-on-main: git failed
- PASS rejects wrong-check-head: Run event or head_sha
- PASS rejects wrong-workflow: Wrong workflow
- PASS rejects fork-run: Run repository
- PASS rejects pull-request-run: Run event
- PASS rejects success-then-failed-rerun: Latest run attempt
- PASS rejects rerun-in-progress: Latest run attempt
- PASS rejects body-hash-mismatch: Body hash mismatch
- PASS rejects empty-body: must name DCB
- PASS rejects missing-package: Expected exactly 26
- PASS rejects wrong-version-package: must be version
- PASS rejects template-not-later-than-library-tag: strictly later than the library tag
- PASS rejects template-not-later-than-library-release: strictly later than the library release
- PASS rejects library-preexisting-package: already exists
- PASS rejects template-preexisting-package: already exists
- PASS rejects library-retry-changed-tag: differs from triggering tag object
- PASS rejects template-retry-changed-tag: differs from triggering tag object
- PASS rejects library-lightweight-tag: must be an annotated tag
- PASS rejects template-lightweight-tag: must be an annotated tag
- PASS rejects library-wrong-peel: does not point at the validated merged SHA
- PASS rejects template-wrong-peel: peeled commit does not match
- PASS rejects library-release-draft: non-draft
- PASS rejects library-release-25-assets: exactly 26 package assets
- PASS rejects library-release-wrong-body: body does not exactly match
- PASS rejects authority-mismatch: expected
- PASS rejects late-library-release-draft: non-draft
- PASS rejects late-library-release-25-assets: exactly 26 package assets
- PASS rejects late-library-release-wrong-body: body does not exactly match
- PASS rejects late-library-release-recreated: strictly later than the library release
- PASS rejects late-library-release-deleted: Unable to read live library GitHub Release
- PASS rejects late-library-wrong-peel: Library tag does not point at the validated merged SHA
- PASS rejects template-guard-missing-token: wrong token
- PASS rejects reader-blob-mismatch: record bytes do not match
- PASS rejects reader-commit-mismatch: Host commit does not match
- PASS rejects reader-tree-blob-mismatch: does not bind
- PASS rejects reader-truncated-tree: Host tree is truncated
- PASS rejects reader-intermediate-tree-mismatch: Host tree is truncated
- PASS rejects reader-intermediate-wrong-type: does not bind
- PASS rejects validator-tree-chain-mismatch: Host tree is mismatched
- PASS rejects validator-tree-chain-incomplete: Host tree chain is incomplete
- All offline tests passed.

## Validator/reader self-test cases from consumer sequence

- PASS reader recursive listing truncated but path walk succeeds 10.22.0
- PASS validator prepared 10.22.0 (older main ancestor; successful rerun attempt 2)
- PASS validator complete 10.22.0
- PASS reader recursive listing truncated but path walk succeeds 42.7.3
- PASS validator prepared 42.7.3 (older main ancestor; successful rerun attempt 2)
- PASS validator complete 42.7.3
- PASS incomplete 10.23.0 history drift before: rejects as required
- PASS incomplete 10.23.0 history drift between: rejects as required
- PASS incomplete 10.23.0 history drift after: equal at 10.23.1
- PASS reader recursive listing truncated but path walk succeeds 10.23.1
- PASS validator prepared 10.23.1 (older main ancestor; successful rerun attempt 2)
- PASS validator complete 10.23.1
- PASS rejects wrong-merged-sha: merged_sha does not equal
- PASS rejects merged-sha-not-on-main: git failed
- PASS rejects wrong-check-head: Run event or head_sha
- PASS rejects wrong-workflow: Wrong workflow
- PASS rejects fork-run: Run repository
- PASS rejects wrong-repository: Run repository
- PASS rejects pull-request-run: Run event
- PASS rejects success-then-failed-rerun: Latest run attempt
- PASS rejects rerun-in-progress: Latest run attempt
- PASS rejects body-hash-mismatch: Body hash mismatch
- PASS rejects empty-body: must name DCB
- PASS rejects duplicate-alias: duplicate or unknown
- PASS rejects missing-alias: Exactly four
- PASS rejects failed-job: Latest attempt job
- PASS rejects wrong-job-attempt: Latest attempt job
- PASS rejects schema-2: requested schema-3
- PASS rejects wrong-record-version: requested schema-3
- PASS rejects draft-complete-release: non-draft
- PASS rejects wrong-complete-url: non-draft
- PASS rejects wrong-complete-peel: does not peel
- PASS rejects reader-blob-mismatch: record bytes do not match
- PASS rejects reader-commit-mismatch: Host commit does not match
- PASS rejects reader-tree-blob-mismatch: does not bind
- PASS rejects reader-truncated-tree: Host tree is truncated
- PASS rejects reader-intermediate-tree-mismatch: Host tree is truncated
- PASS rejects reader-intermediate-wrong-type: does not bind
- PASS rejects validator-tree-chain-mismatch: Host tree is mismatched
- PASS rejects validator-tree-chain-incomplete: Host tree chain is incomplete
- All offline tests passed.

## Optional macOS production approximation

The production helper started the exact restore/build commands from the changed
worktree (HEAD still 32af652d, library source unchanged), using the repository's
floating SDK selection: local SDK 11.0.100-preview.3.26207.106. Restore ran and
Release build began, including production package-on-build tasks. The last visible
output was creation of MaterializedView.Orleans build-output packages. The solution
build then remained stalled and was cancelled (exit 130). It never reached the
explicit 26-package pack block: no production `out` feed was produced. No public
10.23.0 package downloads or public/local comparison were performed. This is an
incomplete local approximation, not a failed real reproducibility gate and not a
26/26 proof. No settings, SBOM option or guard were changed to bypass the stall.

## Sandbox boundaries and remaining gates

- No GitHub workflow was run, dispatched, pushed or published by this builder.
- The public 10.23.0 exact-tag rebuild at the GitHub workspace path and the two independent GitHub runners cannot be verified locally. Their first real execution will be PR CI. Both results remain required before merge; a failure requires stop/report, including all differing packages and SDK/image compared with run 37483069890. Exclusions are not widened.
- The private prepared host record, immutable variable, environment credentials, four checks on the merged commit and stage check are after-merge operator steps; no host record was edited.
- Aspire MCP discovery was attempted but the tool was blocked by the approval policy; no AppHost was used or changed.
- The referenced host design file is absent from this clone; the supplied incident and binding design were used.
