# Sekiban.Pure 10.0.0

Sekiban.Pure 10.0.0 moves Sekiban.Pure to .NET 10 and .NET 11. It is a compatibility release: **no new features, and no change to stored data. The only API differences from 9.0.0 are two changes in the Orleans xUnit test base (see "Before you upgrade").** All twelve packages are released together as 10.0.0.

## Support policy

Sekiban.Pure is in maintenance mode. From this release on it receives security fixes only: fixes for vulnerabilities in Sekiban.Pure itself, and dependency updates that resolve published security advisories. The twelve packages are audited for known vulnerabilities in CI (on pull requests that touch Pure, and weekly). For new projects we recommend Sekiban DCB. Sekiban.Pure 9.0.0 remains available on nuget.org for .NET 9.

## What changed

- **Target frameworks:** `net10.0` and `net11.0`. `net9.0` is no longer targeted. `Sekiban.Pure.SourceGenerator` still targets `netstandard2.0`.
- **Dependencies:**

| Dependency | 9.0.0 | 10.0.0 |
|---|---|---|
| Microsoft.Orleans.* | 9.2.1 | 10.4.2 |
| Microsoft.EntityFrameworkCore | 9.0.8 | 10.0.15 |
| Npgsql.EntityFrameworkCore.PostgreSQL | 9.0.4 | 10.0.3 |
| Microsoft.Extensions.* and System.Configuration.ConfigurationManager | 9.0.8 | 10.0.15 |
| ResultBoxes | 0.3.31 | 0.4.0 |

  Unchanged: Dapr 1.15.4, Microsoft.Azure.Cosmos 3.53.1, Newtonsoft.Json 13.0.3. New: `Sekiban.Pure.Orleans.xUnit` now depends on Humanizer.Core 2.14.1.

## Before you upgrade

1. **Orleans 9 to 10 is a whole-cluster upgrade.** Do not run Sekiban.Pure 9.x (Orleans 9) and Sekiban.Pure 10.0.0 (Orleans 10) silos in one cluster. Upgrade every silo and client together and follow the Orleans 10 upgrade guidance.
2. **Orleans 10 enforces a type allow-list for its default JSON grain-state serializer.** If your host persists Sekiban grain state with Orleans' default JSON serializer, reading it can fail with a `CodecNotFoundException`. Register `NewtonsoftJsonSekibanOrleansSerializer`, as `docs/llm/10_orleans_setup.md` and the samples show. The Orleans test bases of `Sekiban.Pure.Orleans.xUnit` and `Sekiban.Pure.Orleans.NUnit` already do this.
3. **Two source-level changes in `Sekiban.Pure.Orleans.xUnit`**, on `SekibanOrleansTestBase<TDomainTypesGetter>`. They are the only differences found when the public API was compared with 9.0.0:
   - `Configure(ISiloBuilder)` is now an explicit implementation of `ISiloConfigurator`. Code that calls it on the test base type must call it through `ISiloConfigurator`.
   - `ConfigureServices(IServiceCollection)` is now `protected virtual` (it was `public virtual`). Overrides must be declared `protected override`.
4. **Retarget and update together.** Change your projects to `net10.0` or `net11.0` and update all `Sekiban.Pure.*` package references to 10.0.0 at once.
5. **ResultBoxes 0.4.0.** ResultBoxes types appear in the public API of Sekiban.Pure. If your own code uses ResultBoxes directly, read its 0.4.0 changes before you upgrade.

## What was checked

- **Public API:** each package was compared with the published 9.0.0 package for net10.0 and net11.0 (23 comparisons); the only findings are the two listed above.
- **Stored data:** event, snapshot, Orleans grain-state and Cosmos DB event documents written by 9.0.0 are read correctly by 10.0.0, and 10.0.0 writes the same bytes (apart from one header byte that records the operating system in a gzip-compressed snapshot).
- **PostgreSQL:** a database created by 9.0.0 and one created by 10.0.0 (on .NET 10 and on .NET 11) have identical schemas, there are no pending migrations or model changes, and rows written by either version are read identically by the other.
- **Tests:** every Pure test project passes on net10.0 and net11.0 in CI, including the PostgreSQL and Cosmos DB tests.
- **Samples:** `OrleansSekiban` (Orleans with PostgreSQL), `MemStat.Net.Web` and `MemStat.Console` start and work on the new versions.

## What was not checked

- **Dapr.** The `Sekiban.Pure.Dapr` unit tests pass on net10.0 and net11.0, but no Dapr sidecar was started for this release and the Dapr samples (`DaprSample`, `DaprSample2`) have not been run on these versions. If you run Sekiban.Pure on Dapr actors, test in your own environment before you upgrade production.
- **Templates.** `Sekiban.Pure.Templates` is not updated by this release. It still generates `net9.0` projects that reference older preview packages (`1.0.1-preview21`).

## Packages

`Sekiban.Pure`, `Sekiban.Pure.AspNetCore`, `Sekiban.Pure.CosmosDb`, `Sekiban.Pure.Dapr`, `Sekiban.Pure.NUnit`, `Sekiban.Pure.Orleans`, `Sekiban.Pure.Orleans.NUnit`, `Sekiban.Pure.Orleans.xUnit`, `Sekiban.Pure.Postgres`, `Sekiban.Pure.ReadModel`, `Sekiban.Pure.SourceGenerator`, `Sekiban.Pure.xUnit`, all 10.0.0.
