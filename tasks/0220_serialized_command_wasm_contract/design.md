# Serialized Command Contract for WASM (Sekiban-side Design)

## Context
Sekiban already has strong serialized boundaries for projection paths:
- `SerializableEvent`
- `SerializableTagState`
- TagState primitive accumulator abstraction

However, command execution is still typed-first (`ISekibanExecutor` + `ICommandContext`).
That is correct for native C# usage, but it is not enough for a WASM-first command runtime where:
- state is fetched as serialized payload,
- command logic may run in WASM,
- event payloads are returned serialized,
- commit should not require typed event deserialization in Sekiban Core.

This task defines the Sekiban-side contract needed before implementing full WASM local/remote command execution in SekibanWasmRuntime.

## Goal
Add a **serialized command boundary** in Sekiban Core that allows:
1. local command path (client-side WASM execution + server-side serialized commit),
2. remote command path (server-side WASM execution + in-proc serialized commit),
while keeping existing `ISekibanExecutor` behavior unchanged.

## Non-goals
- Replacing existing `ISekibanExecutor`.
- Changing existing public typed command/query APIs.
- Implementing full WASM runtime in this repository.

## Required Outcome
A new executor contract dedicated to serialized command workflows, plus Orleans implementation and tests.

## Proposed Contracts

### 1) New core interface
```csharp
public interface ISerializedSekibanDcbExecutor
{
    Task<ResultBox<SerializableTagState>> GetTagStateAsync(
        TagStateId tagStateId,
        CancellationToken cancellationToken = default);

    Task<ResultBox<SerializedCommitResult>> CommitSerializableEventsAsync(
        SerializedCommitRequest request,
        CancellationToken cancellationToken = default);
}
```

### 2) New DTOs
```csharp
public sealed record SerializedCommitRequest(
    IReadOnlyList<SerializableEventCandidate> Events,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string>? ConsistencyTags,
    IReadOnlyDictionary<string, string>? ExpectedLastSortableUniqueIds,
    string CommandName,
    string Producer);

public sealed record SerializableEventCandidate(
    string EventPayloadName,
    byte[] PayloadUtf8Json);

public sealed record SerializedCommitResult(
    bool Success,
    Guid? LastEventId,
    string? LastSortableUniqueId,
    IReadOnlyList<Guid> EventIds,
    string? ErrorCode,
    string? Message);
```

## Why this shape
- `SerializableEvent` itself includes id/sortable-id/metadata. Those must be server-generated.
- Request uses `SerializableEventCandidate` to prevent trust issues around externally supplied event metadata.
- Concurrency should align with Sekiban reservation semantics, not version-only checks.

## Concurrency model
Use `ExpectedLastSortableUniqueIds` + `ITagConsistentActorCommon.MakeReservationAsync`.

Behavior:
- If expected sortable-id mismatches current consistency state for any required tag, fail fast with conflict.
- On failure after partial reservation, cancel reservations.
- On successful write, confirm reservations.

## Command Context strategy (important)
Do not expose existing typed `ICommandContext` directly to WASM.

Introduce serialized-focused context for WASM command paths (contract name can vary):
- state read as serialized tag state,
- append event as serialized payload + tags,
- no typed payload required at Sekiban boundary.

This keeps domain code clean when paired with WASM-side shared pre-deserialization helpers.

## WASM-side expectation (for compatibility)
WASM runtime should follow this pattern:
1. decode serialized state/event payload at command entry,
2. keep typed state in WASM instance memory,
3. apply multiple operations without repeated encode/decode,
4. serialize only at output boundary.

Sekiban must provide contract + commit semantics so this optimization remains possible.

## Implementation slices

### Slice 1: Core contracts
- Add interface and DTOs in `Sekiban.Dcb.Core`.
- Keep additive only.

### Slice 2: Orleans implementation
- Add `OrleansSerializedSekibanDcbExecutor` (name tentative).
- Implement `GetTagStateAsync` via current tag state actor path.
- Implement `CommitSerializableEventsAsync` with reservation + write + confirm/cancel.

### Slice 3: DI wiring
- Register default implementation in Orleans runtime extensions.
- No replacement of existing executors.

### Slice 4: Tests
- happy path (single/multi event)
- mismatch conflict
- reservation cancellation on write failure
- event ids/sortable id returned correctly

## Acceptance criteria
1. New serialized executor contract exists and is documented.
2. Orleans implementation passes unit/integration tests.
3. Existing typed APIs and tests remain green.
4. Contract is sufficient for SekibanWasmRuntime local/remote command models without additional Sekiban API changes.

## Risks
- Divergence between typed and serialized command semantics.
- Incorrect reservation rollback logic under partial failures.
- Metadata inconsistency if producer/command naming policy is unclear.

## Open decisions
1. Final interface name: `ISerializedSekibanDcbExecutor` vs `ISekibanDcbSerializedExecutor`.
2. Whether to accept both sortable-id and version hints for transition period.
3. Whether `Producer` is client-provided or server-enforced.
