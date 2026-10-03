# Cosmos emulator tests

Real `CosmosDbEventStore` reader exclusion and Cosmos engine batch, conditional patch,
ETag, rollback, and operation-limit characterisation. No production fence is implemented.
Targets .NET 9 and .NET 10; requires Docker locally or an external emulator.

```sh
dotnet test dcb/tests/Sekiban.Dcb.CosmosDb.Emulator.Tests/Sekiban.Dcb.CosmosDb.Emulator.Tests.csproj -p:GenerateSBOM=false
```

The fixture uses Gateway mode, `LimitToEndpoint=true`, HTTPS (`PROTOCOL=https`),
and disabled certificate validation for the emulator's self-signed certificate.
Explorer and telemetry are disabled. Each test owns and deletes its database.
Only `DockerUnavailableException` becomes a local skip. Pull failures, readiness
timeouts, and client/configuration failures fail the tests.
`SEKIBAN_COSMOS_EMULATOR_REQUIRED=1` also makes Docker unavailability fail (CI sets it).
CI runs this project separately from the main DCB test jobs, and audits both TRX
files for nonzero execution, all passed, and zero skipped.

Optionally set `SEKIBAN_COSMOS_EMULATOR_ENDPOINT` to an external HTTPS emulator
using the standard emulator key. This bypasses Docker; connection failures still
fail, including in required mode. Never point this fixture at a production account.

Pinned image:

`mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:vnext-EN20260907@sha256:2db1f9e74c506bcf6fc347aa937aea1c00fa756061296a5a9efba530ce86ec02`

Verified against the MCR v2 registry manifest API: the SHA-256 of the raw dated-tag
manifest matches the pin and its media type is a Docker v2 manifest list. It contains:

- linux/amd64: `sha256:7aec069728dcbd5502d108370b869ab86fe63767dc8f869ad03efb88a76d4036`
- linux/arm64: `sha256:90dde64d09c908fa257fb9009d9df02e5ce931c9f930d53b2549cc22bdfe8833`

Both platform manifests and their image configs were fetched and hash-verified.
Explicit `docker pull --platform linux/arm64` and `--platform linux/amd64`
with the full pinned reference both succeeded.

## Uncovered areas

- ORDER BY page boundaries: vNext ignores `MaxItemCount` for ordered queries.
  Small requested page sizes here exercise the reader API, not actual page boundaries.
- Composite-index enforcement: vNext accepts but drops composite indexes.
- Agreement with production Azure Cosmos DB: emulator behaviour is characterised,
  production agreement is inferred. Neither classic nor Azure is run in this slice.
- HTTP status codes are pinned, not substatus codes or ETag formats.

The tests use public APIs only; the provider grants this project no friend
access. The sweep is constructed through its public DI registration and uses
the real repair runner. The test waits, without sleeps, for the sweep's
terminal log event and requires it to be the completed event.
