# SEK-G49 durable serialization fixtures

`old-0.24.3-net8.json` and `old-0.24.3-net9.json` are frozen durable vectors
written by `CompatibilityWorker.cs` while it targeted the corresponding TFM
and directly restored all six relevant Sekiban packages at `0.24.3`.

The validator deliberately does not compare JSON text. It rebuilds the shared
worker against the current `0.25.0` net10 package feed and verifies old event
and snapshot vectors semantically. It then writes a net10 vector and asks real
`net8.0` and `net9.0` processes with the pinned `0.24.3` package graph to read
it. The worker asserts interface/derived runtime types, null omission,
case-insensitive property names, event/snapshot metadata, and the actual
Cosmos, Dynamo, and Postgres serialization mappings.

The validator's mutation changes a frozen snapshot saved-version field. It
must fail at the semantic metadata assertion; a text-only fixture comparison
would not satisfy this gate.
