# DCB release runbook

The library tag is `dcb-v{V}` and the template tag is `dcbTemplates-v{V}`.
Both workflows accept the same schema-3 `prepared` record. Release-integration
work changes the version authorities and four bilingual release bodies together,
before either tag. Keep review and closeout evidence in the host intent records;
they are not publication-gate inputs.

1. Merge the release-integration PR to main. Record its merged commit as
   `merged_sha` and its PR number as `candidate_pr`. All five
   `SekibanDcbTemplateVersion.props` authorities must agree on `{V}`.
2. Dispatch `run_test_dcb.yml`, `dcb_azure_queue_packaged_consumer.yml` and
   `dcb_template_validation.yml` on that exact commit. A dispatch uses the branch
   head; if main has moved, create a temporary branch at `merged_sha` and
   dispatch the checks from it. The commit must remain an ancestor of main.
   `run_test_dcb.yml` also runs the Cosmos emulator job: the **whole run** must
   conclude success. Handle a flake by re-running the failed job. Attempts are
   not pinned; a successful retry is accepted, while a later failed or running
   retry invalidates the recorded success.
3. Write one host record at
   `intents/sekiban/releases/dcb-v{V}-release-record.json`:
   `schema_version: 3`, `version`, `stage: prepared`, `merged_sha`,
   `candidate_pr`, `checks`, and `release_bodies`. `checks` contains exactly
   `dcbTestsNet9`, `dcbTestsNet10`, `packagedConsumer`, `templateConsumer`, each
   with its numeric `run_id`. The first two aliases normally use the same run.
   The latter aliases map to `packaged-consumer` in the Azure Queue workflow
   and `Pack, install, generate, restore, build, and test templates` in the
   template workflow. Record the SHA-256 of each checked-in body as
   `library_en_sha256`, `library_ja_sha256`, `template_en_sha256`,
   `template_ja_sha256`. Bodies must be non-empty, name `{V}`, and have no TODO
   or TBD. Japanese bodies contain Japanese text; template bodies identify
   `template` / `テンプレート`.
4. Commit the host record and set `vars.SEKIBAN_RELEASE_RECORD_REF` to that
   immutable 40-hex host commit. `SEKIBAN_RELEASE_RECORD_TOKEN` reads only the
   private host record; workflow tokens read this repository's Actions state
   and live tags/releases. Never move the variable during an active tag run.
5. **Mandatory before any tag:** dispatch `dcb_release_record_check.yml` on
   **main**, with `version={V}`, `state=prepared`, `ref` exactly equal to the
   variable, and `candidate=merged_sha`. Require success. This publishes
   nothing and is the first real check of the host reader and token. Do not
   infer credential validity from offline fixtures.
6. Obtain operator approval for the irreversible tag/publication steps. Verify
   that all 26 library versions and the template tag are absent. Create and
   push the annotated library tag at `merged_sha`. The push launches the
   library workflow, which checks the exact source manifest, packs 26
   artifacts, validates package identities/dependency groups and the Azure
   Queue consumer, then checks the live tag and first-attempt absence before
   NuGet push. It waits for public visibility, proves public/local semantic
   equality, then creates a non-draft GitHub Release with exactly 26 packages
   and `cat library.en.md library.ja.md` as its body.
7. Keep the variable on `prepared`. After library publication and its release
   succeed, obtain operator approval and create/push the annotated template
   tag at the same `merged_sha`. Its tagger date must be strictly later than
   both the library tagger date and library release `published_at`. The
   workflow reads these facts live, verifies the library release's 26 assets
   and exact bilingual body, all 26 public package versions and five version
   authorities, packs and tests template consumers, checks unchanged content
   for duplicate-safe retries and first-attempt template absence, then pushes.
   It waits for template visibility and proves public/local equality before
   the non-draft template Release using `cat template.en.md template.ja.md`.
8. After both publications succeed, change the host record to `complete`,
   retaining the prepared fields and adding `published.library_release_url`
   and `published.template_release_url` with the canonical GitHub URLs for
   the two tags. Commit, update the variable, and dispatch the stage check on
   main with `state=complete`. This checks both tags peel to `merged_sha` and
   both URLs resolve to non-draft releases. Complete closeout in host intent
   records separately. Remove any temporary dispatch branch when finished.

## Irreversible actions and recovery

Tag pushes trigger publication; NuGet pushes and public GitHub Releases are
irreversible release operations. Stop if any reader, validator, live guard,
visibility or equality check fails. Never bypass a guard, move an existing tag,
or substitute another package under the same version.

A transient failure can be retried on the same run/tag object and identical
source/artifacts. Later workflow attempts require the live tag object to equal
the triggering tag object. Duplicate-safe pushes still require public/local
semantic equality. After **any public artifact exists**, a source-changing fix
requires a **new version** and a new release-integration PR, checks, record and
tags. Do not re-use the partially published version.

The `dcb-release` environment remains the token/publication boundary. Stage
checks run only on main; tag workflows run on their respective tag series.
Offline verification never proves environment configuration, private-host
credential validity, or actual public publication.
