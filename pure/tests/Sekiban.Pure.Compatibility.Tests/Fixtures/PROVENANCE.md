# Published 9.0.0 serialized-data fixtures

Generated once from nuget.org PackageReferences to `Sekiban.Pure`,
`Sekiban.Pure.Orleans`, `Sekiban.Pure.CosmosDb`, and
`Sekiban.Pure.SourceGenerator`, all exactly **9.0.0**. The generator is kept in
`../FixtureGenerator/` for audit and is deliberately absent from every solution.
It shares `Domain.cs` and `FixedInputs.cs` with the tests. Both builds use assembly
name `CompatibilityDomain` and the SDK default assembly version `1.0.0.0`.
The tests use project references to the new code, including the source generator.

Commands, from the repository root (with the cache environment from the builder):

```sh
dotnet build pure/tests/Sekiban.Pure.Compatibility.Tests/FixtureGenerator/FixtureGenerator.csproj -c Release --source https://api.nuget.org/v3/index.json --disable-build-servers -m:1 -nr:false
env -u DOTNET_ROLL_FORWARD dotnet pure/tests/Sekiban.Pure.Compatibility.Tests/FixtureGenerator/bin/Release/net9.0/CompatibilityDomain.dll pure/tests/Sekiban.Pure.Compatibility.Tests/Fixtures
```

Actual generator output: `RuntimeInformation.FrameworkDescription` = `.NET 9.0.9`;
`typeof(System.Text.Json.JsonSerializer).Assembly.GetName().Version` = `9.0.0.0`.
`DOTNET_ROLL_FORWARD` was unset. The generated runtimeconfig targets `net9.0`,
requires `Microsoft.NETCore.App` and `Microsoft.AspNetCore.App` `9.0.0`, and has no
roll-forward policy. The generator also checks the runtime and JSON assembly major
versions and refuses to overwrite existing fixture files.
The baseline-only build reports NU1608: the published generator resolves Roslyn
Common 4.14.0 alongside Workspaces.Common 4.5.0's exact 4.5.0 dependency.

## Fixed inputs

- Aggregate Guid: `11111111-2222-3333-4444-555555555555`.
- Event Guid: `aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee`.
- Command/event/payload timestamp: `2025-09-22T12:34:56Z` (`DateTimeKind.Utc`).
- SortableUniqueId (literal, no random generation): `06388890769600000000012345678901`.
  Its first 19 digits encode UTC ticks `638889076960000000`, used by
  `EventDocument.FromEvent` for the Cosmos document timestamp.
- Command: `RegisterAccount` with the aggregate Guid, timestamp, and name
  `Sample account 日本語 <>&`.
- Partition keys: aggregate Guid above, group `AccountProjector`, root `compatibility`.
- Event version / aggregate version: `1`.
- Causation id: `22222222-3333-4444-5555-666666666666`.
- Correlation id: `compatibility-correlation`; executed user: `fixture-user`.
- Projector: `AccountProjector`, version `1.0.0`; payload type: `Account`.
- Snapshot payload assembly version: `1.0.0.0`.

The aggregate is produced by projecting the fixed event onto an empty aggregate.
The snapshot uses `SerializableAggregate.CreateFromAsync`, including its production
GZip compression. Each Orleans grain-storage fixture serializes that snapshot
through the named production `IGrainStorageSerializer` implementation.
The Cosmos document is `EventDocument<AccountRegistered>.FromEvent`, the concrete
document shape sent by `CosmosDbEventWriter`. It goes through
`SekibanCosmosSerializer(domainTypes.JsonSerializerOptions)`, exactly as
`CosmosDbFactory.GetCosmosClientAsync` assigns `ClientOptions.Serializer`.
The domain types are created by the package source generator with default options
(case insensitive, camelCase) and no test-only serializer or option overrides.

All files contain the exact UTF-8 output bytes, without a BOM or trailing newline.
No normalization is applied, including to compressed payload bytes.

## SHA-256

```text
3b75fe6e434fd4b683bb0bc69d8571778594f88b67aeb7d20d54ac7f3aa3d216  event-envelope.json
ef22efc3ae2351016e2fe463e45e0ef11e965144cffb5c9970e0f88222d8d570  aggregate-snapshot.json
f76bfebc338632d480efffd4db139ff0119cd3acdec8fbc7101f8b3ea3aea530  orleans-system-text-json.json
f76bfebc338632d480efffd4db139ff0119cd3acdec8fbc7101f8b3ea3aea530  orleans-newtonsoft-json.json
2482249cdfa37764803a7bd8ea244da1f3c298325818541055df6d0485c709cc  cosmos-event-document.json
```
