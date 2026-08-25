# SEK-G48 slice A baseline

These inventories were captured from `origin/core_main` at
`0ef2407757cf00f19ed5ce27fede3ffcc767d0cf` before the root authority was
introduced. The validator evaluates all 69 C# projects after each change and
requires an exact match with `evaluated-target-frameworks.tsv`.

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
