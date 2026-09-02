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

## SEK-G52 serialized-commit interop fixtures

These are literal, no-final-newline UTF-8 JSON witnesses for the shared wire decision: the server wire is runtime V1;
the TypeScript client model is an adapter input, not a second server dialect. `interop_manifest.json` is the runner
catalogue (and intentionally is not self-hashed); every payload fixture below is checked against this table by both the
C# and dependency-free Node runners.

- Source Sekiban commit: `f53ffdc69e225433b266cc1f92875d6b2b11aa93`.
- Contract version: `runtime-v1`.
- Comparison exclusion: client-only `eventId` is excluded from every client-to-wire comparison. It is never emitted on
  official V1 output and is not server-assigned from this request shape.
- R1 permits byte equality only for standard padded base64 that decodes to valid UTF-8 JSON with no BOM and no nested
  `eventType`, `eventName`, or `eventPayloadName` member. R2 is the documented JavaScript canonicalization profile;
  R3 is a typed client bind error, never a claimed equality.

| Fixture | UTF-8 bytes | SHA-256 | Source commit | Contract | Comparison exclusion / expected outcome |
| --- | ---: | --- | --- | --- | --- |
| `interop_official_v1_populated.json` | 341 | `7d20127220732280adf9614f88cf19e5a532478bec836dcbb876359ec4ca07ed` | `f53ffdc69e225433b266cc1f92875d6b2b11aa93` | `runtime-v1` | `eventId`; R1 byte-identical |
| `interop_legacy_populated.json` | 329 | `6aaaf17d79eae2a9709330f0f4de5bbad561b291fb739cf714cea1928e2b1b01` | `f53ffdc69e225433b266cc1f92875d6b2b11aa93` | `runtime-v1` | `eventId`; legacy-compatible |
| `interop_legacy_explicit_empty.json` | 43 | `52d11a52f7657262da74a613935aad9cdda2cf4f2191b3d838b56c3e2aa85439` | `f53ffdc69e225433b266cc1f92875d6b2b11aa93` | `runtime-v1` | `eventId`; explicit-empty legacy-compatible |
| `interop_ts_client_model.json` | 392 | `5f518fe4707e0b5f3087125b5febbac100b4cdf8d0b970bd29d1c2ac361ccd30` | `f53ffdc69e225433b266cc1f92875d6b2b11aa93` | `runtime-v1` | `eventId`; paired R1/R2 positive |
| `interop_r2_canonical_positive.json` | 249 | `d15bbf67e08a424590cc213ca87befa18fc8e4560a37f416c3b4ea9527ae4cd5` | `f53ffdc69e225433b266cc1f92875d6b2b11aa93` | `runtime-v1` | `eventId`; R2 byte-exact positive |
| `interop_r2_canonical_positive_v1.json` | 239 | `fa932e5435975c7f433dc0b748b44e92a9bcbc4be981add7d363d1cc4fbd7683` | `f53ffdc69e225433b266cc1f92875d6b2b11aa93` | `runtime-v1` | `eventId`; R2 expected V1 output |
| `interop_r2_integer_like_key.json` | 166 | `721ba8fff375932ced886e6561b1bd338ebe500ddb1f5192aa1fb1ecd68d6b81` | `f53ffdc69e225433b266cc1f92875d6b2b11aa93` | `runtime-v1` | `eventId`; R2 key-order loss |
| `interop_r2_numeric_lexical_loss.json` | 213 | `eff7c7b2fd99dd196b4eacd48e0ce4e2179a6637db1628a64d753b4c3c26315e` | `f53ffdc69e225433b266cc1f92875d6b2b11aa93` | `runtime-v1` | `eventId`; R2 numeric loss |
| `interop_r2_duplicate_key.json` | 178 | `f21c0828be96f135cbc3669fb9b6d160440a1680736534a53560a71838f46eef` | `f53ffdc69e225433b266cc1f92875d6b2b11aa93` | `runtime-v1` | `eventId`; R2 typed duplicate-key error |
| `interop_r3_bom_payload.json` | 144 | `1a35320ceebeb7ae8ba64f0b61ae4d51987514960a5afba9df64c47f49ef8533` | `f53ffdc69e225433b266cc1f92875d6b2b11aa93` | `runtime-v1` | `eventId`; R3 typed BOM error |
| `interop_r3_non_json_payload.json` | 135 | `18368fff92a30e0e48d09fdd776b582798dd38a5450c7fa88d19e175661910ec` | `f53ffdc69e225433b266cc1f92875d6b2b11aa93` | `runtime-v1` | `eventId`; R3 typed non-JSON error |
| `interop_r3_invalid_utf8_payload.json` | 132 | `5e21f31f3bf20ba6f00cb47a90437bd74301aae99b73a3708b27e0758f0c4f27` | `f53ffdc69e225433b266cc1f92875d6b2b11aa93` | `runtime-v1` | `eventId`; R3 typed invalid-UTF-8 error |
| `interop_client_empty_tag.json` | 156 | `a4ad2a5952118e06c21e7690f41dbd91509531bea0b65e156f8e5c4b8e5d3206` | `f53ffdc69e225433b266cc1f92875d6b2b11aa93` | `runtime-v1` | `eventId`; typed empty-tag error |
| `interop_client_duplicate_consistency.json` | 274 | `1c6f676c17c179c6969b14adad17586a5c00d464bd5d0ce199ee4d6167e123e4` | `f53ffdc69e225433b266cc1f92875d6b2b11aa93` | `runtime-v1` | `eventId`; typed duplicate-consistency error |
| `interop_response_member_vocabulary.json` | 480 | `6403336400221c2ad3fca101beb7d8cdfd54471990dd7961ec0c8328218cb160` | `f53ffdc69e225433b266cc1f92875d6b2b11aa93` | `runtime-v1` | response-member vocabulary |
