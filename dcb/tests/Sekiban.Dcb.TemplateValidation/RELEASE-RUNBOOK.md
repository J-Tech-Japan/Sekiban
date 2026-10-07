# DCB release runbook

Libraries and templates release in two steps on two commits. Tags remain
`dcb-v{V}` and `dcbTemplates-v{V}`; templates may use the library commit itself
or a later main commit containing it. Never move an existing tag.

## Library release

1. Merge the release-integration PR: move all five
   `SekibanDcbTemplateVersion.props` authorities and the template README to V,
   and add the reviewed EN/JA **library** notes. Template notes are optional;
   if present in PR CI they must form a complete, valid pair.
2. On the exact merged commit, dispatch `run_test_dcb.yml`,
   `dcb_azure_queue_packaged_consumer.yml`, and `dcb_template_validation.yml`.
   Leave `public_libraries` off for the library phase. If main has moved, use
   a temporary branch at the candidate for these checks. It must remain on main.
   The whole run, including Cosmos and every other job, must succeed; re-run
   all jobs after a flake. Latest attempts are read live, so a later failed or
   running retry invalidates evidence. Partial job retries are not evidence.
3. Write `intents/sekiban/releases/dcb-v{V}-release-record.json` in the host:
   schema 3, version V, stage `prepared`, `merged_sha`, non-negative
   `candidate_pr`, exactly `dcbTestsNet9`, `dcbTestsNet10`, `packagedConsumer`,
   `templateConsumer` with numeric `run_id`, and `release_bodies` with
   `library_en_sha256`, `library_ja_sha256`. Optional legacy template hashes
   are ignored; no other members are allowed. Bodies name V, are non-empty,
   have no TODO/TBD, and Japanese bodies contain Japanese text.
4. Commit the host record, set `vars.SEKIBAN_RELEASE_RECORD_REF` to that
   immutable host commit, and never move it during a tag run. The record token
   reads only the host; the workflow token reads live runs, jobs, tags and releases.
5. Mandatory before tagging: dispatch `dcb_release_record_check.yml` on main
   with `kind=library`, `version=V`, `state=prepared`, `ref` equal to the
   variable, and `candidate=merged_sha`. Require success. Main's dispatched
   tooling inspects a separate full candidate checkout, including older layouts.
6. Obtain operator approval. Require all 26 public library versions and the
   current-version template tag absent; push an annotated library tag at the
   candidate. The workflow checks the record, source manifest, bodies, 26 packs,
   identities/dependencies and Azure Queue consumer, then the live tag and
   first-attempt absence before push. Visibility and public/local equality
   precede the non-draft Release with 26 exact assets and `cat en ja` body.
7. After successful publication, retain all prepared fields, set library stage
   `complete`, and add `published.library_release_url` (canonical tag URL).
   A legacy `template_release_url` is allowed and ignored. Commit the host,
   update the variable and require `kind=library`, `state=complete` stage check.
   Only the library tag must peel to this record's `merged_sha`.

## Template adjustment and release

1. After V is public, audit the templates against best practice for a person
   starting on released V. Adjust the templates and add reviewed EN/JA template
   notes in a PR; merge while all five authorities still read V. Once authorities
   move to V+1, templates for V are skipped. Accepted lag is the operator's call.
   PRs naming public V run both local-head and public-V consumers. A breaking
   library change that cannot pass both requires an authority bump and library
   notes for V+1 in that PR, ending V's template window.
2. Dispatch `dcb_template_validation.yml` on the exact template commit with
   **`public_libraries=true`**. Require the whole run and every other job green.
   The `Published-library template consumer` must succeed after actually running
   against nuget.org; a skipped job is rejected. PR runs cannot be record evidence.
3. Add `intents/sekiban/releases/dcbTemplates-v{V}-release-record.json`:
   schema 3 with the same top-level prepared members, template candidate's
   `merged_sha` and `candidate_pr`, exactly `templatePublicConsumer` with its
   dispatched run ID, and exactly `template_en_sha256`, `template_ja_sha256`.
   Template notes must name V and template / テンプレート, contain no TODO/TBD,
   and include Japanese text in the Japanese body. Keep the library record
   present and unchanged in this host commit. Point the variable at that commit.
4. Require main stage check with `kind=template`, `state=prepared`, candidate
   equal to the template commit and ref equal to the variable. Obtain operator
   approval, then push the annotated template tag at that candidate. Tagger date
   must be strictly later than the library tagger date and Release `published_at`.
5. Before push the tag workflow verifies template record/HEAD/main containment,
   live successful public-consumer evidence and template body hashes; annotated
   template tag identity; annotated library tag with live/local peel agreement
   and ancestry; non-draft library Release with 26 exact asset names and body
   read from the verified library **SHA**; all 26 public versions; five authority
   parity; nuget.org consumer; chronology; first-attempt template absence and
   unchanged package on retry. Push is followed by visibility, semantic equality
   and the non-draft template Release. Every gate failure stops publication.
6. Set only the template record to `complete`, retain prepared fields and add
   exactly `published.template_release_url`. Keep the library record unchanged,
   commit the host, move the variable and require the template complete stage
   check. It checks the template tag against the template record's commit.

For **10.23.1**, keep the existing library candidate `20e02544` and its recorded
checks. First set the library record to complete at host commit H1, point the
variable at H1, and stage-check `kind=library`, `state=complete`, candidate
`20e025441cd95cc831c3bdc1bb901fc92929e77f`. After template adjustment at later
commit T, create host commit H2 containing that **unchanged complete library
record plus the prepared template record**. Point the variable at H2; stage-check
`kind=template`, `state=prepared`, candidate T, then obtain approval and tag T.
Keep H2 during the tag run. Finally H3 retains the library record unchanged and
makes the template record complete; move the variable to H3 and stage-check
`kind=template`, `state=complete`, candidate T. A prepared library check at the
original candidate also works with main's tooling and the original prepared host
record. Do not move the library tag or rebuild its record at T.

The scheduled drift check compares only the latest stable tag versions:
library ahead emits a notice and succeeds; template ahead fails; equal succeeds.
It does not inspect authorities. Incomplete 10.23.0 history needs no exemption.
This builder unit creates no tags, dispatches or publications.

## Irreversible actions and recovery

Tag pushes trigger publication; NuGet pushes and public GitHub Releases are
irreversible release operations. Stop if any reader, validator, live guard,
visibility or equality check fails. Never bypass a guard, move an existing tag,
or substitute another package under the same version.

Before re-running a failed tag run, dispatch `dcb_release_reproducibility.yml`
for that released library version on GitHub-hosted runners. Require 26/26 public
packages to equal the clean rebuild. If it fails, do not retry: report all differing
packages (distinguishing download failures and missing local files) and the SDK
and runner image beside those of the original run, then use a new version. For
this PR, the 10.23.0 result remains required before merge unless the operator
explicitly decides otherwise. Never widen the exclusions or relax another guard.

Retry promptly on the same run/tag object. A retry **re-packs** the exact tagged
commit; it does not reuse the original build output. Equality ignores entries
starting with `_manifest/` at the package root in addition to the existing ZIP
relationship, signature and core-properties exclusions. It compares assemblies,
nuspec, README and every other canonical entry, including `lib/_manifest/x`.
The generated SBOM is the only new exclusion. Later attempts require the live
tag object to equal the triggering tag object. Duplicate-safe pushes skip existing
packages; public/local equality must still pass before the GitHub Release.
The library retry also requires the current-version template tag to be absent.

All three visibility waits are **60 minutes** (3600 seconds), including the
pre-pack library wait in the template workflow. In the 10.23.0 incident it took
about **38 minutes from the start of the run** until all 26 packages were visible:
push finished at 15:01 UTC and all were visible by 15:37 UTC on 2026-10-06.
Neither tag job sets a shorter job timeout; the GitHub default is 360 minutes.

The Release action updates an existing GitHub Release on retry. Its assets are
the re-packed `.nupkg` files, whose SBOM bytes differ from the NuGet copies.
Symbol packages (`.snupkg`) are outside equality and outside the Release assets;
this check does not prove recovery of symbol publication.

The two-runner comparison catches non-determinism between runners at one point
in time; drift over time (SDK or image changes) is caught only by the released-
artifact reproducibility workflow and this retry procedure. Both tag workflows
and the two real-artifact gates log `dotnet --info` and the runner image version.
SDK pinning and retaining SDK/image diagnostics beyond Actions log retention are
candidates for later work, not changes in this unit.

After an artifact is public, a source-changing fix to **that artifact** requires a
new version, release-integration PR, checks and record. Adjusting unpublished
templates after their library is public is the normal template procedure.

The `dcb-release` environment remains the token/publication boundary. Stage
checks run only on main; tag workflows run on their respective tag series.
Offline verification never proves environment configuration, private-host
credential validity, or actual public publication.

Raise the stale currency-fixture version when templates begin using a newer library API so that the stale consumer still restores and builds before currency validation rejects it.
