# Serialization & Domain Types

> **Navigation**
> - [Core Concepts](01_core_concepts.md)
> - [Getting Started](02_getting_started.md)
> - [Commands, Events, Tags, Projectors](03_aggregate_command_events.md)
> - [MultiProjection](04_multiple_aggregate_projector.md)
> - [Query](05_query.md)
> - [Command Workflow](06_workflow.md)
> - [Serialization & Domain Types](07_json_orleans_serialization.md) (You are here)
> - [API Implementation](08_api_implementation.md)
> - [Client UI (Blazor)](09_client_api_blazor.md)
> - [Orleans Setup](10_orleans_setup.md)
> - [Storage Providers](11_storage_providers.md)
> - [Testing](12_unit_testing.md)
> - [Common Issues and Solutions](13_common_issues.md)
> - [ResultBox](14_result_box.md)
> - [Value Objects](15_value_object.md)
> - [Deployment Guide](16_deployment.md)

Serialization in DCB is explicit: you register every event, tag, projector, query, and state payload via
`DcbDomainTypes`. This removes reflection surprises and enables code generation for Orleans.

## DcbDomainTypes Catalog

`DcbDomainTypes` aggregates six registries plus shared `JsonSerializerOptions` (`src/Sekiban.Dcb/DcbDomainTypes.cs`).
Use the builder to register your domain types.

```csharp
public static DcbDomainTypes GetDomainTypes() =>
    DcbDomainTypes.Simple(types =>
    {
        types.EventTypes.RegisterEventType<StudentCreated>();
        types.TagStatePayloadTypes.RegisterPayloadType<StudentState>();
        types.TagProjectorTypes.RegisterProjector<StudentProjector>();
        types.TagTypes.RegisterTagGroupType<StudentTag>();
        types.MultiProjectorTypes.RegisterProjector<WeatherForecastProjection>();
        types.QueryTypes.RegisterListQuery<GetStudentListQuery>();
    });
```

### JSON Options

- Defaults to camelCase, non-indented output.
- Override via `DcbDomainTypes.Simple(builder => { ... }, jsonOptions: customOptions)`.
- Event stores rely on these options for serialization; keep them consistent across services.

## Orleans Serialization

DCB leverages Orleans Source Generators for tag state payloads and query results. Annotate records with
`[GenerateSerializer]` and add `[Id(n)]` attributes when necessary.

- Tag state payload example: `internalUsages/Dcb.Domain/Student/StudentState.cs`
- MultiProjection responses: `internalUsages/Dcb.Domain/Projections/WeatherForecastItem.cs`

For event payloads you can use either Orleans serialization or System.Text.Json; they are serialized by the event store.

`Sekiban.Dcb.Orleans` customizes Orleans serialization via
`NewtonsoftJsonDcbOrleansSerializer` for backward compatibility (`src/Sekiban.Dcb.Orleans/NewtonsoftJsonDcbOrleansSerializer.cs`).

## Event Metadata and Sortable IDs

Events are wrapped in `SerializableEvent` before persistence. Payloads are serialized bytes accompanied by the payload
name so the runtime can deserialize without dynamic type discovery (`tasks/dcb.design/records.md`).

`SortableUniqueId` encodes timestamp + entropy to preserve order even across distributed nodes
(`src/Sekiban.Dcb/Common/SortableUniqueId.cs`). Use the helpers when generating ids.

## Tag Identification

Tags serialize as strings `"Group:Content"`. Implement `ITag` or convenience interfaces to ensure reversible
serialization. For hierarchical tags include separators in the content (e.g., `tenant/customerId`).

## Custom JSON Contexts

If you need advanced converters (e.g., for value objects), register them in the shared `JsonSerializerOptions` passed to
`DcbDomainTypes`. The executor reuses those options when serializing commands for logging and when events travel through
the publisher.

## Versioning Strategy

- **ProjectorVersion** – bump when tag projector logic changes; forces cache invalidation.
- **MultiProjectorVersion** – bump when the read model schema changes; grains rebuild from scratch.
- **Json Contracts** – version query results at the type level (e.g., `WeatherForecastCountResultV2`).

Keep backward compatibility in mind—older Blazor clients may call the API during rollout.

## Troubleshooting

- Missing type registration yields runtime errors like "Event type not registered" when executing commands.
- JSON mismatches manifest as deserialization failures in event store backends. Log the payload name from `EventMetadata`
  to track down the offending type.
- Orleans may require a full rebuild if new `[GenerateSerializer]` types were added.

## Serialized Commit Wire Contract (SEK-G17)

This is the NORMATIVE specification of the official serialized-commit wire contract used by the WASM boundary.

### Canonical owner

The contract is owned by these types in the `Sekiban.Dcb.Core` package:

- `Sekiban.Dcb.Commands.SerializedCommitRequest` — the request envelope (positional record).
- `Sekiban.Dcb.Events.SerializableEventCandidate` — one event candidate.
- `Sekiban.Dcb.Commands.ConsistencyTagEntry` — one consistency reservation entry.
- `Sekiban.Dcb.Actors.ISerializedSekibanDcbExecutor.CommitSerializableEventsAsync` — the accepting operation.

Any endpoint that claims to speak this contract MUST conform to the shape and serializer settings below and to the frozen
golden vectors (`SerializedCommitWireGoldenTests`).

### JSON shape

```json
{
  "eventCandidates": [
    {
      "payload": "<base64 of the UTF-8 event payload JSON>",
      "eventPayloadName": "<registered event type name>",
      "tags": ["Group:Content", "..."]
    }
  ],
  "consistencyTags": [
    { "tag": "Group:Content", "lastSortableUniqueId": "<sortable-unique-id, or \"\">" }
  ]
}
```

| Field | Type | Required | Notes |
| --- | --- | --- | --- |
| `eventCandidates` | array | yes (may be empty) | Ordered; each element is written as one event in order. An empty array is a valid empty commit. |
| `eventCandidates[].payload` | string | yes | Base64 of the UTF-8 bytes of the event payload JSON. Opaque to the commit path (stored verbatim). |
| `eventCandidates[].eventPayloadName` | string | yes | Registered event type name used to resolve/validate the payload. |
| `eventCandidates[].tags` | string[] | yes (may be empty) | **Per-event tags are AUTHORITATIVE.** Each event keeps its OWN tag list; tags are never flattened or shared across events. Format `"Group:Content"`. |
| `consistencyTags` | array | yes (may be empty) | Optimistic-concurrency reservations. Every `tag` here must also appear in some event candidate's `tags`. |
| `consistencyTags[].tag` | string | yes | `"Group:Content"`. |
| `consistencyTags[].lastSortableUniqueId` | string | yes | Last observed `SortableUniqueId` for the reservation; empty string means "no prior expectation". |

### Serializer normalization (fully pinned)

The production wire bytes are pinned by the contract-owned serializer
`Sekiban.Dcb.Commands.SerializedCommitWireContract.Options` (backed by the source-generated
`SerializedCommitWireJsonContext`). The settings are:

- **Property naming**: camelCase.
- **Property order**: declaration / constructor-parameter order.
- **Indentation**: none. UTF-8, no BOM, no insignificant whitespace.
- **Encoder**: `JavaScriptEncoder.Default` — non-ASCII and HTML-sensitive characters are `\uXXXX`-escaped (byte-for-byte
  identical to the ASP.NET `JsonSerializerDefaults.Web` write path).
- **Null/default handling**: values are always written (never ignored).
- **`byte[]` payloads**: base64 strings.

Pinning is **additive only**: it lives in the source-gen context, NOT as attributes on the positional DTOs. Adding
serialization attributes to the DTOs is forbidden — even a baseline-neutral `[JsonPropertyName]` would change the output of
consumers that serialize with a fresh `JsonSerializerOptions` (which still emits PascalCase). Golden vectors freeze both the
contract-serializer bytes and the fresh-options PascalCase bytes so any drift fails CI.

### Additive versioned envelope + two-phase acceptance

The official shape has no version discriminator. SEK-G17 adds an explicitly versioned envelope WITHOUT touching the legacy
shape:

- `Sekiban.Dcb.Commands.VersionedSerializedCommitRequest(int Version, IReadOnlyList<SerializableEventCandidate>
  EventCandidates, IReadOnlyList<ConsistencyTagEntry> ConsistencyTags)`, `CurrentVersion = 1`. (G15's single-event
  `SerializedConditionalCommitRequest` is intentionally NOT reused as the base envelope.)

Acceptance is optional and additive via `ISerializedCommitAcceptor` / `SerializedCommitAcceptor` (no member is added to any
existing interface). It is two-phase:

1. **Phase 1 — raw discrimination** (`SerializedCommitVersionDiscriminator`): the `version` property is read straight from
   the raw UTF-8 bytes, before any typed payload binding, base64 decode, tag reservation, EventId allocation, or
   executor/store call. The discriminator is the **exact** ordinal property name `version` (the camelCase spelling of the
   contract). Matching is deliberately **case-sensitive** and never uses ambient case-insensitivity.
   - No `version` and no case-variant of it → legacy path.
   - One integer exact `version` == 1 → known version.
   - One integer exact `version` != 1 → **`UnsupportedSerializedCommitEnvelopeVersionException`** (fail closed, before side effects).
   - A **case-variant** of `version` (e.g. `Version` / `VERSION` / `vErSiOn`), whether alone or alongside the exact one,
     does NOT silently select V1 or legacy — it is a **`MalformedSerializedCommitException`** (`AmbiguousVersionCasing`).
   - Non-object root, non-integer `version`, or a duplicated exact `version` → **`MalformedSerializedCommitException`** (a
     DISTINCT typed shape error). The typed error is **secret-safe**: it carries only a closed reason code and a fixed
     message, never the offending JSON, keys, payload/base64, type names, or a raw parser exception.
2. **Phase 2 — bind + route**: only the resolved shape is bound. A missing version is the legacy official shape, lifted
   losslessly to V1 by `LegacyUnversionedSerializedCommitAdapter` (per-event tags preserved; no per-commit-tag model
   involved). A known version binds `VersionedSerializedCommitRequest`. A binding failure (including a malformed V1 payload)
   is reported as a typed `MalformedSerializedCommitException`, never a null-reference. Either path routes the same event
   candidates + consistency tags to `ISerializedSekibanDcbExecutor.CommitSerializableEventsAsync` with identical semantics.

### Ownership and compatibility-claim guidance

- The official contract (`eventCandidates` + base64 `payload` + `eventPayloadName` + **per-event `tags`** +
  `consistencyTags`) has been stable across dcb-v10.2.2 → 10.6.0 and needs no migration for 10.1.x programs.
- A different `events` / `payloadJson` / per-commit-`tags` shape is a SEPARATE downstream runtime contract (e.g. a WASM
  runtime or an as-a-service host). It is NOT this contract and must not be described as mirroring it.
- Any endpoint claiming compatibility with this contract must conform to the spec above and pass the golden vectors.
- A downstream adapter that collapses per-event tags into per-commit tags may do so ONLY when every event in the commit
  carries an identical tag set, and must reject any other commit explicitly rather than silently dropping tags.
