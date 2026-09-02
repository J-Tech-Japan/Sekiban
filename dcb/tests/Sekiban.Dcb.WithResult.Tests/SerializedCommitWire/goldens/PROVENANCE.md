# Frozen serialized-commit wire artifacts

These committed UTF-8 JSON files are the FROZEN AUTHORITY for the SEK-G17 behavioral no-migration
proof. Tests read these exact bytes directly and drive them through the public
`SerializedCommitAcceptor` -> real executor/store. They are NOT generated at test runtime; a change
to the current DTO JSON shape or producer output cannot silently update them — it makes the current
producer diverge from these bytes, which the drift-guard test catches.

## Contract
Official serialized-commit wire contract: `Sekiban.Dcb.Commands.SerializedCommitRequest`
(`eventCandidates` + base64 `payload` + `eventPayloadName` + per-event `tags` + `consistencyTags`).

## Provenance
- Tag: `dcb-v10.1.17` (commit `a90e6197091c3e6958bb7209dfebd6a12ebc6c65`).
- Wire DTO source blobs, byte-identical between `dcb-v10.1.17` and current HEAD:
  - `dcb/src/Sekiban.Dcb.Core/Commands/SerializedCommitRequest.cs` = `b6d5290c8e4eb1807d416836d2caecee62e7e7b0`
  - `dcb/src/Sekiban.Dcb.Core/Events/SerializableEventCandidate.cs` = `600efe913b67058bfd29db78a2d28210ba842a67`
  - `dcb/src/Sekiban.Dcb.Core/Commands/ConsistencyTagEntry.cs` = `f4d4f1a26c96e6bb3286188f35e6949d87b9544c`
- Serializer settings: System.Text.Json `JsonSerializerDefaults.Web` (camelCase, default encoder, no
  indentation), UTF-8, NO BOM, no insignificant whitespace.
- Dataset: two `StudentCreated` events with heterogeneous per-event tags (event #1 two tags, event #2
  one tag); the empty file is the empty-commit shape.

## Pinned integrity (SHA-256, lowercase hex)
- `legacy_1017_unversioned.json` — 531 bytes — `26c103ab7c8f117de809711a7b31f26d37ef374c1e551bc1ad0948e4105a17cf`
- `legacy_1017_empty.json` — 43 bytes — `52d11a52f7657262da74a613935aad9cdda2cf4f2191b3d838b56c3e2aa85439`
- `ts_client_aliased_unversioned.json` — 86 bytes — `327f845705080b9d69819640217b0cec3aac0933c6f65b8575cacff414c5eec6`
  (SEK-G51 frozen negative fixture: the exact executed consult JSON
  `{"candidates":[{"eventPayloadName":"X","payload":{},"tags":["T:1"]}],"consistency":[]}`; the TypeScript-client
  top-level aliases `candidates` / `consistency` must fail closed before execution rather than producing the legacy empty
  commit. The fail-closed and gate-removed mutation proof both load this exact resource.)
