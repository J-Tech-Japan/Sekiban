# SEK-G49 durable serialization fixtures

`old-0.24.3-net8.json` and `old-0.24.3-net9.json` are frozen durable vectors
written by `CompatibilityWorker.cs` while it targeted the corresponding TFM
and directly restored all six relevant Sekiban packages at `0.24.3`.

The validator deliberately does not compare JSON text. It rebuilds the shared
worker against the current `0.25.0` net10 package feed and verifies old event
and snapshot vectors semantically. Each vector carries the actual
`AssemblyQualifiedName` of its derived event; the net10 writer and both real
`net8.0` and `net9.0` pinned reader processes resolve it with `Type.GetType`.
It then writes a net10 vector and asks those old processes to read it. The
worker asserts interface/derived runtime types, null omission, case-insensitive
property names, event/snapshot metadata, and the actual Cosmos, Dynamo, and
Postgres serialization mappings.

The validator's mutations change a frozen snapshot saved-version field and the
frozen runtime type identity. The former must fail at the semantic metadata
assertion; the latter must fail at the runtime `Type.GetType` assertion in the
net10 and both pinned old reader processes. A text-only fixture comparison
would not satisfy this gate.
