# Design: Remove DcbDomainTypes from Orleans Grains (WASM-Friendly Boundary)

## Purpose (Why we are doing this)

This work exists to make **projection execution swappable** (native C# now, WASM later) without rewriting Orleans Grain orchestration.

Concretely:

- Orleans Grains should become "infrastructure only": stream subscription, catch-up, timers, persistence, and routing.
- All domain-specific logic and .NET-only dependencies must be pushed behind a seam so that a future WASM-backed implementation can be added.

This is not about running Orleans in WASM. It is about preventing **C#-domain implementation details** from leaking into Grain code.

## Definition of Done (Exit Criteria)

This task is "done" when all of the following are true:

1. `dcb/src/Sekiban.Dcb.Orleans.Core/Grains/MultiProjectionGrain.cs` no longer references:
   - `DcbDomainTypes`
   - `JsonSerializerOptions`
   - `IServiceProvider`
   - `IQueryCommon` / `IListQueryCommon`
   - `IMultiProjectionPayload`
   - `Event` (for projection execution; the Grain should forward `SerializableEvent`)
2. Snapshot persistence in the Grain is **opaque bytes only**:
   - Grain stores/restores `byte[]` provided by the host
   - No `JsonSerializer.Serialize/Deserialize(..., _domainTypes.JsonSerializerOptions)` remains in the Grain
3. Query execution path in the Grain is host-driven:
   - Grain forwards `SerializableQueryParameter`
   - Grain receives `SerializableQueryResult` / `SerializableListQueryResult`
   - No query deserialization inside the Grain
4. Backward compatibility is preserved for native execution:
   - Existing snapshots can still be restored (native host keeps legacy format initially)
   - Query results match existing behavior for representative projections/queries
5. Integration wiring is complete:
   - DI registers the new host factory
   - Existing hosts/apps build without manual wiring changes

Validation should include at least:

- Build succeeds for the solution/projects affected by Orleans runtime.
- A basic smoke test: activate a `MultiProjectionGrain`, ingest a batch, persist snapshot, deactivate/reactivate, restore, and query.

## Goal

Remove `DcbDomainTypes` from Orleans Grain constructors/fields (especially `MultiProjectionGrain`, `TagStateGrain`, `TagConsistentGrain`) and stop Grains from directly touching:

- Domain "god objects": `DcbDomainTypes`
- .NET-only infrastructure leaking into "engine boundary": `JsonSerializerOptions`, `IServiceProvider`
- Deserialized C# domain payloads: `Event` with `IEventPayload`, `IQueryCommon` / `IListQueryCommon`, `IMultiProjectionPayload`

The long-term intent is to allow the projection engine implementation to be swapped (native C# vs WASM) without rewriting the Grain orchestration logic.

## Non-goals (for this design doc)

- Making Orleans itself run inside WASM (Grains remain .NET).
- Solving tag projections in WASM now. (We keep a clean seam so tags can follow later.)
- Enforcing a cross-language ABI at the interface level (we are still .NET); instead we keep interfaces "data-only" so a WASM-backed host can implement them.

## Boundary Definition (what must stay clean)

The **Grain should only depend on an engine-agnostic, data-only API**. Anything that depends on:

- `DcbDomainTypes`, reflection over domain types, query handler registration
- `JsonSerializerOptions` / System.Text.Json model types
- .NET DI (`IServiceProvider`)

must be moved behind that seam into the *native* implementation, or into a future *WASM* implementation.

Visually:

```
┌──────────────────────────────────────────────────────┐
│ Orleans Grain (orchestration)                         │
│ - stream subscription / catch-up / timers             │
│ - persistence of opaque snapshot bytes                │
│ - routes queries to correct projector                 │
│ Depends only on:                                      │
│   - SerializableEvent / SerializableQuery* DTOs       │
│   - IProjectionActorHost (+ factory)                  │
├──────────────────────────────────────────────────────┤
│ IProjectionActorHost (engine-agnostic, data-only)     │  ← Clean seam
│ - accepts SerializableEvent batches                   │
│ - returns snapshot bytes and SerializableQuery*       │
│ - returns metadata (versions / ids) as primitives     │
├──────────────────────────────────────────────────────┤
│ Native host (wraps current C# actor + domain types)   │
│ - owns DcbDomainTypes / JsonSerializerOptions / DI    │
│ - wraps GeneralMultiProjectionActor                   │
├──────────────────────────────────────────────────────┤
│ Future: WASM host (wraps WASM module)                 │
│ - owns WASM instance / memory / imports               │
└──────────────────────────────────────────────────────┘
```

---

## Current State Analysis

### Types that must not be referenced by Grain code

| Type | Where used | Why problematic |
|------|-----------|-----------------|
| `DcbDomainTypes` | Grain constructors, Actor constructors | God-object, purely native C# |
| `JsonSerializerOptions` | Snapshot serialization, query result serialization | System.Text.Json, .NET-specific |
| `IServiceProvider` | `IProjectionRuntime.ExecuteQueryAsync` | .NET DI, cannot exist in WASM |
| `Event` (with `IEventPayload`) | `IProjectionRuntime.ApplyEvent` | `IEventPayload` is a deserialized C# object |
| `IQueryCommon` / `IListQueryCommon` | `IProjectionRuntime.ResolveProjectorName` | C# interface for deserialized query |
| `IMultiProjectionPayload` | `IProjectionState.GetSafePayload()/GetUnsafePayload()` | C# interface for projection state |

### Types that can be used at the clean seam (data-only)

| Type | Fields | WASM-safe? |
|------|--------|------------|
| `SerializableEvent` | `byte[] Payload, string SortableUniqueIdValue, Guid Id, EventMetadata, List<string> Tags, string EventPayloadName` | Yes (all primitives/bytes) |
| `SerializableQueryParameter` | `string QueryTypeName, byte[] CompressedQueryJson, string QueryAssemblyVersion` | Yes |
| `SerializableQueryResult` | `string ResultTypeName, string QueryTypeName, byte[] CompressedResultJson, ...` | Yes |
| `SerializableListQueryResult` | Similar byte[]-based fields | Yes |
| State metadata | versions/ids as primitives | Yes |

---

## Proposed Design

### 1) Grain-facing abstraction: `IProjectionActorHost`

The Grain should not create `GeneralMultiProjectionActor` directly and should never deserialize queries/events into C# payload objects. Instead, it talks to an engine-agnostic host that operates on DTOs (`SerializableEvent`, `SerializableQueryParameter`, `SerializableQueryResult`) and opaque snapshot bytes.

```csharp
/// <summary>
/// Grain-facing projection host.
/// Must be engine-agnostic and avoid .NET DI, JsonSerializerOptions, or domain payload types.
/// </summary>
public interface IProjectionActorHost
{
    // Event ingestion (Grain receives SerializableEvent from Orleans stream already)
    Task AddSerializableEventsAsync(
        IReadOnlyList<SerializableEvent> events,
        bool finishedCatchUp = true);

    // Metadata only; nullable values are allowed for "no events yet".
    Task<ResultBox<ProjectionStateMetadata>> GetStateMetadataAsync(
        bool includeUnsafe = true);

    // Snapshot is opaque bytes (format is owned by the implementation).
    Task<ResultBox<byte[]>> GetSnapshotBytesAsync(
        bool includeUnsafe = true);

    // Returns success/failure only. The snapshot format is implementation-owned.
    Task<ResultBox<bool>> RestoreSnapshotAsync(byte[] snapshotData);

    // Query execution (query and results are DTOs)
    Task<ResultBox<SerializableQueryResult>> ExecuteQueryAsync(
        SerializableQueryParameter query);

    Task<ResultBox<SerializableListQueryResult>> ExecuteListQueryAsync(
        SerializableQueryParameter query);

    // Controls/state checks needed by orchestration
    void ForcePromoteBufferedEvents();
    Task<string> GetSafeLastSortableUniqueIdAsync();
    Task<bool> IsSortableUniqueIdReceivedAsync(string sortableUniqueId);
    long EstimateStateSizeBytes();
}
```

Notes:

- No `IServiceProvider` in method signatures. The native implementation can use DI internally via constructor injection.
- No `JsonSerializerOptions`. Snapshot serialization/deserialization is owned by the host implementation.
- No `CancellationToken` at the seam. Orleans can still cancel internally, but we keep the interface "data-only".

### 2) Metadata DTO: `ProjectionStateMetadata`

```csharp
public sealed record ProjectionStateMetadata(
    string ProjectorName,
    string ProjectorVersion,
    bool IsCatchedUp,
    // Unsafe ("latest") state
    int UnsafeVersion,
    string? UnsafeLastSortableUniqueId,
    Guid? UnsafeLastEventId,
    // Safe state
    int SafeVersion,
    string? SafeLastSortableUniqueId);
```

### 3) Factory: `IProjectionActorHostFactory`

Grains should create hosts via a factory so the engine can be swapped without changing Grain code.

```csharp
public interface IProjectionActorHostFactory
{
    IProjectionActorHost Create(
        string projectorName,
        GeneralMultiProjectionActorOptions? options = null);
}
```

The factory itself is resolved via DI. The native factory captures `DcbDomainTypes`, `IServiceProvider`, etc internally. The Grain never sees them.

### 4) Routing / projector-name resolution (query-to-projector)

Today the Grain resolves projector name using `IQueryCommon`/`IListQueryCommon` after deserializing the query. That must disappear from Grain code.

At the seam, routing can be done from `SerializableQueryParameter.QueryTypeName` (string). Two options:

1. Put routing into the host: `host.ExecuteQueryAsync(query)` implicitly routes because Grain already activated per projector.
2. Put routing into a small router interface (recommended for clarity):

```csharp
public interface IProjectorQueryRouter
{
    // Input is the DTO; output is projectorName string.
    ResultBox<string> ResolveProjectorName(SerializableQueryParameter query);
}
```

Native router implementation can use `DcbDomainTypes.QueryTypes` internally. A future WASM router could consult WASM-exported metadata.

### 5) Native implementation strategy (wrap, don't rewrite yet)

We keep existing business logic in `GeneralMultiProjectionActor` initially.

- `NativeProjectionActorHost` wraps `GeneralMultiProjectionActor`.
- It is responsible for:
  - converting `SerializableEvent` to internal `Event` if needed (native only)
  - executing queries using native query handlers + DI internally
  - producing snapshot bytes (envelope and payload) and restoring from them
  - exposing only metadata to the Grain

This delivers the immediate goal: **remove `DcbDomainTypes` and `JsonSerializerOptions` from `MultiProjectionGrain`** without forcing a full rewrite of the actor/runtime internals.

### 6) Future: make the engine itself WASM-friendly (optional Phase 2+)

Once the Grain is clean, we can refactor deeper so that the host does not need to materialize native `Event` / query objects:

- Introduce a WASM-friendly engine runtime (can reuse and evolve the existing `IProjectionRuntime`):
  - `ApplyEvents` consumes `SerializableEvent` bytes
  - query execution consumes `SerializableQueryParameter` bytes
  - state is represented as an opaque handle + metadata

This can be done without changing Grain code again because the Grain only sees `IProjectionActorHost`.

---

## Tag Projections (Scope Clarification)

Tags are not required for the immediate goal (issue #906 is focused on projection grains). For now:

- Keep tag grains native-only, or apply the same host pattern later.
- Avoid introducing a "clean seam" interface that returns concrete C# actor classes (that defeats the purpose).

---

## MultiProjectionGrain Migration (Concrete Targets)

### Current problematic dependencies in `MultiProjectionGrain`

`dcb/src/Sekiban.Dcb.Orleans.Core/Grains/MultiProjectionGrain.cs` currently uses:

- `_domainTypes` for projector version, query execution, snapshot JSON serialization, size estimation.
- `_eventRuntime` and/or `_domainTypes.EventTypes` for stream event deserialization.
- `JsonSerializer.Serialize(..., _domainTypes.JsonSerializerOptions)` inside the Grain.

### Target (after migration)

```csharp
public class MultiProjectionGrain : Grain, IMultiProjectionGrain
{
    private readonly IProjectionActorHostFactory _actorFactory;
    private IProjectionActorHost? _host;

    public MultiProjectionGrain(
        ...,
        IProjectionActorHostFactory actorFactory,
        ...)
    {
        _actorFactory = actorFactory;
    }
}
```

The Grain becomes:

- stream subscriber and catch-up orchestrator
- persistence owner of `byte[] snapshot` only
- query dispatcher that forwards DTOs

### Key “delete-from-Grain” moves

- Query execution: move *query deserialization* and *handler execution* behind `IProjectionActorHost`.
- Snapshot persistence: Grain stores opaque `byte[]` only; encoding/decoding lives behind `IProjectionActorHost`.
- Event application: Grain forwards `SerializableEvent` directly; host decides how to interpret it.

### Query execution simplification (what the Grain should look like)

```csharp
await EnsureInitializedAsync();
await StartSubscriptionAsync();
if (_host == null) return SerializableQueryResult.Empty;
return await _host.ExecuteQueryAsync(queryParameter);
```

---

## IServiceProvider Handling (Fixing the key contradiction)

Do not pass `IServiceProvider` through any interface that is intended to be engine-agnostic.

- Native implementations can receive `IServiceProvider` via constructor injection (or directly inject the few services they need).
- WASM implementations will not use .NET DI; they will provide their own internal resolution mechanism.

This keeps the seam clean and still allows native query handlers to use DI.

---

## WASM Function Mapping

For future reference, here's how `IProjectionRuntime` methods would map to WASM exported functions:

| Interface Method | WASM Function | Input | Output |
|-----------------|---------------|-------|--------|
| `GenerateInitialState(name)` | `projection_init(name_ptr, name_len)` | UTF-8 string | state handle (i32) |
| `ApplyEvent(name, state, ev, threshold)` | `projection_apply_event(state_handle, event_ptr, event_len, threshold_ptr, threshold_len)` | state handle + serialized event bytes | new state handle |
| `ApplyEvents(name, state, events, threshold)` | `projection_apply_events(state_handle, events_ptr, events_len, threshold_ptr, threshold_len)` | state handle + serialized event batch | new state handle |
| `PromoteBufferedEvents(name, state, threshold)` | `projection_promote(state_handle, threshold_ptr, threshold_len)` | state handle + threshold string | new state handle |
| `ExecuteQueryAsync(name, state, query)` | `projection_query(state_handle, query_ptr, query_len)` | state handle + compressed query bytes | result bytes ptr + len |
| `SerializeState(name, state)` | `projection_serialize(state_handle)` | state handle | bytes ptr + len |
| `DeserializeState(name, data, threshold)` | `projection_deserialize(data_ptr, data_len, threshold_ptr, threshold_len)` | bytes | state handle |
| `GetProjectorVersion(name)` | `projection_version(name_ptr, name_len)` | string | string ptr + len |
| `EstimateStateSizeBytes(name, state)` | `projection_estimate_size(state_handle)` | state handle | i64 |

State should be treated as an opaque handle in the WASM engine. The .NET side can wrap it in a host-owned object and expose only `ProjectionStateMetadata` at the seam.

---

## Implementation Plan (Incremental)

### Phase 1: Introduce host seam and remove domain types from Grain (fast win)

1. Add `IProjectionActorHost`, `ProjectionStateMetadata`, `IProjectionActorHostFactory`.
2. Implement `NativeProjectionActorHost` wrapping `GeneralMultiProjectionActor`.
3. Migrate `MultiProjectionGrain`:
   - remove `DcbDomainTypes` / `IEventRuntime`
   - delete all `JsonSerializer.*(_domainTypes.JsonSerializerOptions)` usage
   - persist/restore using `GetSnapshotBytesAsync` / `RestoreSnapshotAsync`
   - execute queries using `host.ExecuteQueryAsync` / `ExecuteListQueryAsync`

Acceptance criteria:

- `MultiProjectionGrain` compiles without referencing `DcbDomainTypes`, `JsonSerializerOptions`, `IServiceProvider`, `IQueryCommon`/`IListQueryCommon`, `IMultiProjectionPayload`.
- Existing behavior remains identical for snapshot format and query results (because native host preserves legacy encoding).

### Phase 2: Optional deeper cleanup for WASM engine enablement

1. Introduce/adjust a WASM-friendly projection engine runtime (can evolve `IProjectionRuntime`):
   - consume `SerializableEvent`
   - route queries using `SerializableQueryParameter.QueryTypeName`
   - avoid `IServiceProvider` in signatures
2. Refactor `GeneralMultiProjectionActor` internals to depend on that engine runtime instead of `DcbDomainTypes`.

This phase can proceed without touching Grains again.

---

## Files to Modify/Create

### New files:
1. `Runtimes/IProjectionActorHost.cs`
2. `Runtimes/IProjectionActorHostFactory.cs`
3. `Runtimes/NativeProjectionActorHost.cs`
4. `Runtimes/NativeProjectionActorHostFactory.cs`
5. `Runtimes/ProjectionStateMetadata.cs`
6. (Optional) `Runtimes/IProjectorQueryRouter.cs` + native implementation

### Modified files:
1. `Grains/MultiProjectionGrain.cs`
2. Host `Program.cs` files — DI registrations for the factory/host
3. Tests

---

## Risk Assessment

| Risk | Severity | Mitigation |
|------|----------|------------|
| `NativeProjectionActorHost` is a large adapter between `IProjectionActorHost` and `GeneralMultiProjectionActor` | High | Incremental: first wrap actor 1:1, then optimize |
| Native query execution still needs DI | Medium | Inject DI into native host internally; do not pass `IServiceProvider` across the seam |
| Snapshot compatibility regressions | High | Keep legacy snapshot encoding in native host initially; add tests that round-trip old snapshots |
| Large number of files affected | Medium | Phase 3B can be done incrementally (one method at a time) |

---

## Summary of Interface Boundaries

```
Grain (orchestration)
  └─ depends only on clean seam:
     - IProjectionActorHost (+ factory)
     - SerializableEvent / SerializableQuery*
     - byte[] snapshot

Clean seam
  ├─ NativeProjectionActorHost (wraps existing C# actor + domain types internally)
  └─ Future: WasmProjectionActorHost (wraps WASM module)
```

The Grain layer sees ONLY:
- `IProjectionActorHost` (event processing, queries, snapshots)
- Primitive types (`string`, `int`, `byte[]`, `Guid`)
- Serializable DTOs (`SerializableEvent`, `SerializableQueryParameter`, `SerializableQueryResult`)

No `DcbDomainTypes`, no `JsonSerializerOptions`, no `IServiceProvider`, no `IQueryCommon`/`IListQueryCommon`, no `IMultiProjectionPayload`.
