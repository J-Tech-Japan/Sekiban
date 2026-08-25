# SEK-G49 slice B compatibility baselines

These inventories characterize the 0.25.0 `net10.0` conversion on top of the
SEK-G48 authority foundation. The validator evaluates all 69 C# projects after
each change and requires an exact match with `evaluated-target-frameworks.tsv`.

The SDK authority is the exact stable `10.0.400` SDK (the current `10.0.x`
band at implementation time); `global.json` and all three CI workflows are
validated together, including a wrong-exact-version negative mutation.

`excluded-projects.tsv` records the 27 Samples and two tools that intentionally
do not consume the core authority. `package-reference-versions.tsv` freezes the
244 C# PackageReference versions, `package-assets.tsv` freezes the twelve
package-producing `lib/` asset sets, and `ci-command-matrix.tsv` freezes every
restore/build/test command and its actual `-f` flag across the three workflows.

`package-nuspecs/` captures the root `.nuspec` from the same full-pack path for
all twelve packages. The validator normalizes only its synthetic package version
and source commit; package identity, framework groups, and dependency metadata
must match exactly.

`validate-net10-api-compat.sh` compares each producer's `net10.0` asset with
its latest published stable `net9.0` asset: 0.24.3 for every `Sekiban.*`
package and 0.1.4 for independently-versioned `MemStat.Net`. No per-API
suppression is approved. `validate-net10-serialization.sh` uses frozen 0.24.3
net8/net9 vectors and real old-target reader processes in both directions.

`validate-net10-indexeddb-browser.sh` packs the current IndexedDb producer,
then has a net10 Blazor WASM consumer call the default
`WebAssemblyHostBuilder.AddSekibanIndexedDb` path. Its browser process imports
the exact packed `_content/Sekiban.Infrastructure.IndexedDb/sekiban-runtime.mjs`
and verifies an IndexedDB write/read round trip; conditional-reference and JS
binding mutants both execute the same gate and must fail.
