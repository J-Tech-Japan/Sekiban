# DCB 10.22 release runbook (SEK-G82 / AC10)

## Annotated tag creation

Create `dcb-v{V}` and `dcbTemplates-v{V}` as annotated tags only after the
`prepared` pointer and its approval are validated. Tag creation for
`refs/tags/dcb-v*` and `refs/tags/dcbTemplates-v*` is restricted to repository
admins by ruleset `23358049` ("DCB release tag creation admin-only").

## `vars.SEKIBAN_RELEASE_RECORD_REF` movement

- Keep the pointer on `prepared` until the library workflow and any same-tag
  retries succeed.
- Move the pointer to `libraries-verified` only before the template tag push.
- Never move the pointer while a tag workflow run is pending.

## Stage-check workflow

Run `.github/workflows/dcb_release_record_check.yml` on `main` and record a
successful run at each stage pointer before the next external action
(`library-tagged/incomplete`, `template-tagged/incomplete`,
`artifacts-verified`, `complete`).

## No re-runs of recorded runs

Do not re-run any recorded check run that is bound into the closed release
record. A re-run permanently invalidates the candidate.

## Up-to-date-before-merge

Immediately before merge, the reviewed PR head must contain the current `main`
tip (`compare/main...{head}` reports `behind_by == 0`, or the merge-base equals
the `main` tip). Otherwise update, re-run CI / PostgreSQL dispatch, and
re-review.

## Implementation-review transport for the release-candidate PR

The PR whose merge becomes the closed schema-v2 `prepared` `candidate` must
finish its exact-head implementation review before merge. Same-account GitHub
reviews stay `COMMENTED` and must carry exactly one canonical semantic verdict
line of the form `Verdict: **APPROVE**` (a leading list marker `- ` is allowed).

Complete the review through intent-cli notify so the host transport matches the
closed validator:

- outbox `from_role` is `review` and `to_role` is `orchestrator` (not
  `reviewer`);
- `result_nonce` is present and identical on the outbox record line, the
  delivered line, and the orchestrator report receipt;
- the orchestrator receipt reaches `report_arrived=true` before merge;
- chronology is GitHub review `submitted_at` < outbox record `created_at` <=
  receipt `reported_at` <= `delivered_at` < merge.

Do not invent or rewrite transport after merge to repair a missing nonce, wrong
role, or open receipt. Cut a new tip-binder PR instead and gather fresh
integrated-head CI on that tip before authoring `prepared`.

### After SEK-G85 (#1249)

Product tip advanced to merge `1bc6d495` with #1244 tombstone query fail-closed.
That PR must **not** be used as the prepared `candidate`: its host notify used
`from_role=reviewer` without `result_nonce`, and the COMMENTED APPROVE targeted
head `84d81acc` rather than merge head `f78abb9f`. Cut a new tip-binder PR from
current `main`, complete exact-head review with `from_role=review` +
`result_nonce` + closed orchestrator receipt before merge, then re-gather tip CI
before authoring `prepared`.

## Pre-tag package absence

Before the library tag push, confirm none of the 26 `{id}/{V}` packages exist
on nuget.org. The library workflow also enforces this on attempt 1 and proves
public-package semantic equality after push on every attempt.

## Stop-and-recover

If any live guard, stage-check, or private-host smoke fails, stop. Do not push
packages, create GitHub Releases, or advance the pointer. Recover by repairing
the candidate and starting a fresh prepared record when required.

## Environment operator gate (`dcb-release`)

The operator has created and verified GitHub Environment `dcb-release` with
custom deployment policies for exactly:

- branch `main`
- tag `dcb-v*`
- tag `dcbTemplates-v*`

Design re-verifies this through the API immediately before this PR merges and
before any token-reading job runs.

Operator verification steps before adding the secret:

1. `GET /repos/J-Tech-Japan/Sekiban/environments/dcb-release`
2. `GET /repos/J-Tech-Japan/Sekiban/environments/dcb-release/deployment-branch-policies`
   — must list exactly the three policies above.
3. Confirm no repository-level secret named `SEKIBAN_RELEASE_RECORD_TOKEN`
   (`GET /repos/J-Tech-Japan/Sekiban/actions/secrets`).
4. Confirm no organization-level secret visible to the repository with that
   name (`GET /repos/J-Tech-Japan/Sekiban/actions/organization-secrets`).

The Environment secret `SEKIBAN_RELEASE_RECORD_TOKEN` is present only on
`dcb-release`. Merge/closeout records this API evidence together with the
up-to-date-before-merge evidence.
