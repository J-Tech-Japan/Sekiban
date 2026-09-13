# SEK-G79 PR #1235 F1-F8 repair

Status: completed.

The repair was made in the existing clean child checkout
`/Users/tomohisa/dev/GitHub/Sekiban-Implement/repair-checkouts/g79-1234-release-train`
on `codex/sek-g79-dcb-1022-release-train`, based on reviewed head
`13e4b0ce7e10fc69413aeb36389d46a0db723395`. The final pushed head is
`71853695e97293ecae01a89a7a511a718548aafd` and the PR is
https://github.com/J-Tech-Japan/Sekiban/pull/1235.

## Repair mapping

- F1: `packagesDcbTemplate.yml` now requires live, non-draft `libraries-verified`
  evidence before template packing: exact `dcb-v10.22.0` tag/release/repository/body,
  peeled head, 26 exact release assets, and library-before-template tag order. The
  release-record fixtures cover false tag order, missing release, draft release, wrong
  body, and 25-asset cases.
- F2: `ReleaseRecordValidator` requires the exact seven-entry CI/review inventory, with
  unique names, expected workflow/event, positive attempt, unsuperseded flag, successful
  conclusion, merged SHA, and strictly ordered start/completion timestamps. The fixture
  matrix covers deletion, rename, duplicate, stale/superseded, wrong event, and wrong
  head mutants.
- F3: release records now require the exact repository/tag/GitHub release URLs, finalized
  release state, exact asset counts, all 26 package identities/versions and NuGet
  flat-container URLs, template identity, repository-root body sources, and all four
  body digests. Negative fixtures cover forged URLs/digests, missing root, and 25 assets.
- F4: timestamp parsing accepts only canonical UTC `Z` timestamps (whole seconds or seven
  fractional digits). Artifact review must precede all four issue closeouts, and complete
  must strictly follow every closeout; equality, non-UTC, missing-review, early-closeout,
  and early-complete fixtures are exercised.
- F5: `validate-release-tags.sh` uses an injectable feed base and finite request timeout,
  downloads each exact nupkg, and verifies nuspec ID/version. The self-test covers the
  positive library/template matrix, missing/wrong/HTTP-success-without-exact-nuspec
  artifacts, and timeout.
- F6: template publication uses `--skip-duplicate`, preceded by an immutable same-version
  package-byte guard. The disposable retry proof shows unchanged content is allowed,
  changed same-version content is rejected with new-version guidance, and first
  publication remains allowed.
- F7: the library and template workflows invoke the executable bilingual body validator.
  EN/JA release bodies state net9/net10, G74-G78, five default publisher attempts,
  opt-in size-gate/durable-recovery boundaries, Orleans 10.3.1 cluster alignment,
  explicit PostgreSQL schema ownership, and no data rewrite.
- F8: both packaged-consumer paths and status composition use NuGet package source
  mapping that confines `Sekiban.Dcb.*` to the supplied local feed. They reject missing
  exact local DCB artifacts before restore; the omission mutant removes Core and must fail.

No product runtime code, package version, tag, package publication, release, issue
closure, or host packet state was changed.

## Local validation

All commands below were run from the child checkout.

- `git diff --check`: passed; final `git status --short`: clean.
- `bash -n dcb/tests/Sekiban.Dcb.TemplateValidation/*.sh` and the Azure Queue consumer
  script: passed.
- `dotnet build dcb/tests/Sekiban.Dcb.TemplateValidation/Sekiban.Dcb.TemplateValidation.csproj -c Release --nologo -p:NuGetAudit=false`:
  passed, 0 warnings and 0 errors.
- `dotnet run --project dcb/tests/Sekiban.Dcb.TemplateValidation/Sekiban.Dcb.TemplateValidation.csproj -c Release --no-restore -- workflow --repo-root "$PWD"`:
  passed; exact 26 effective packable projects and workflow surfaces validated.
- `bash dcb/tests/Sekiban.Dcb.TemplateValidation/validate-release-tags.sh --self-test --repo-root "$PWD"`:
  passed; exact manifest, feed/nuspec matrix, timeout, immutable retry, and all named
  release-record mutants. Output was captured in the validator run log.
- `dcb/tests/Sekiban.Dcb.TemplateValidation/pack-local-dcb.sh --repo-root "$PWD" --output /private/tmp/sek-g79-local-feed.DOf4a5 --version 10.22.0`:
  passed; isolated local feed contains 26 packages.
- `bash dcb/tests/Sekiban.Dcb.TemplateValidation/run-packaged-consumer.sh --repo-root "$PWD" --feed /private/tmp/sek-g79-local-feed.DOf4a5 --version 10.22.0`:
  passed; five generated outputs, 11 bundled test projects, local-feed restore, and
  release-record positive/negative matrix. Log: `/private/tmp/sek-g79-packaged-consumer-final-4.log`.
- `bash dcb/tests/Sekiban.Dcb.TemplateValidation/run-status-composition.sh --repo-root "$PWD" --feed /private/tmp/sek-g79-local-feed.DOf4a5 --version 10.22.0`:
  passed, including the required legacy composition failure control. Log:
  `/private/tmp/sek-g79-status-composition-final.log`.
- `bash dcb/tests/Sekiban.Dcb.Orleans.Tests/run-packaged-consumer.sh --repo-root "$PWD" --feed /private/tmp/sek-g79-local-feed.DOf4a5 --version 10.22.0`:
  passed for both net9.0 and net10.0; output includes `Azure Queue packaged consumer
  passed for net9.0` and `net10.0`. Log: `/private/tmp/sek-g79-orleans-consumer-clean.log`.
- An isolated `dotnet restore/build dcb/Sekiban.Dcb.slnx` attempt was stopped after several
  minutes with no further output; it is not claimed as a local green result. The required
  GitHub DCB net9/net10 jobs below completed successfully.

The initial no-feed consumer attempt correctly failed because 10.22.0 is not yet public;
the successful evidence uses only the disposable local feed and no public write.

## Fresh exact-head CI

All results below are for `71853695e97293ecae01a89a7a511a718548aafd`:

- DCB net9: run `34730477605`, job `103652222973`, passed (15m13s).
- DCB net10: run `34730477605`, job `103652222866`, passed (15m28s).
- Template packaged-consumer: run `34730477610`, job `103652222872`, passed (6m53s).
- Azure Queue packaged-consumer: run `34730477633`, job `103652222842`, passed (4m13s).
- SonarCloud Code Analysis: passed; https://sonarcloud.io/dashboard?id=J-Tech-Japan_Sekiban&pullRequest=1235.
- SonarCloud companion: passed, check run `103653073286`.

The first repair commit (`358d5c1d`) made the F1-F8 changes and passed the runtime/package
checks, but its Sonar quality gate identified five concrete findings. The second commit
(`71853695`) factored the validator, centralized the flagged literals, and moved the
NuGet secret to an environment binding; the fresh exact-head cycle above is green.

## Canonical child lifecycle

The child PR claim was run once before editing:

```json
{"kind":"pr","repo":"J-Tech-Japan/Sekiban","number":1235,"mode":"write","proceed":true,"applied":true,"add_labels":["intent-pr-update-in-progress"],"current_labels":["intent-target","intent-pr-request-update"],"errors":[],"warnings":[],"github_only":true}
```

After push and green fresh CI, canonical completion was run once:

```json
{"kind":"pr","repo":"J-Tech-Japan/Sekiban","number":1235,"outcome":"repair-pushed","mode":"write","proceed":true,"applied":true,"add_labels":["intent-pr-rereview-ready"],"remove_labels":["intent-pr-update-in-progress","intent-pr-request-update"],"errors":[],"warnings":[],"child_cwd":true,"github_only":true}
```

The PR remains open and ready for the independent exact-head review. No merge or release
was performed.
