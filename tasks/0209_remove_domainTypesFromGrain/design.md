# Design: Remove DcbDomainTypes from Orleans Grains (WASM-Compatible)

## Goal

Completely remove the `DcbDomainTypes` field from all Orleans Grain classes. The runtime interfaces (`IProjectionRuntime`, `IEventRuntime`, `ITagProjectionRuntime`) must be **WASM-compatible**, meaning they must NOT reference concrete C# domain types, .NET-specific infrastructure (e.g., `JsonSerializerOptions`, `IServiceProvider`), or any type that cannot exist in a WASM sandbox.

## Design Principle: WASM Boundary

`IProjectionRuntime` and related interfaces serve as the **boundary between the orchestration layer (Grain/Actor)** and the **projection engine (native C# or WASM)**. Everything crossing this boundary must be serializable to primitive types, byte arrays, and strings.

```
┌─────────────────────────────────────────────────┐
│  Orleans Grain (Orchestration)                  │
│  - Stream subscription, catch-up, timers        │
│  - Persist/restore snapshots                    │
│  - Route queries                                │
│  Uses ONLY runtime interfaces ↓                 │
├─────────────────────────────────────────────────┤
│  IProjectionRuntime / IEventRuntime / etc.      │  ← WASM boundary
│  (no concrete C# types, no JsonSerializerOptions│
│   no IServiceProvider, no DcbDomainTypes)       │
├─────────────────────────────────────────────────┤
│  Native Implementation    │  WASM Implementation │
│  (DcbDomainTypes inside)  │  (WASM module inside)│
└─────────────────────────────────────────────────┘
```

---

## Current State Analysis

### Types that CANNOT cross the WASM boundary

| Type | Where used | Why problematic |
|------|-----------|-----------------|
| `DcbDomainTypes` | Grain constructors, Actor constructors | God-object, purely native C# |
| `JsonSerializerOptions` | Snapshot serialization, query result serialization | System.Text.Json, .NET-specific |
| `IServiceProvider` | `IProjectionRuntime.ExecuteQueryAsync` | .NET DI, cannot exist in WASM |
| `Event` (with `IEventPayload`) | `IProjectionRuntime.ApplyEvent` | `IEventPayload` is a deserialized C# object |
| `IQueryCommon` / `IListQueryCommon` | `IProjectionRuntime.ResolveProjectorName` | C# interface for deserialized query |
| `IMultiProjectionPayload` | `IProjectionState.GetSafePayload()/GetUnsafePayload()` | C# interface for projection state |

### Types that CAN cross the WASM boundary

| Type | Fields | WASM-safe? |
|------|--------|------------|
| `SerializableEvent` | `byte[] Payload, string SortableUniqueIdValue, Guid Id, EventMetadata, List<string> Tags, string EventPayloadName` | Yes (all primitives/bytes) |
| `SerializableQueryParameter` | `string QueryTypeName, byte[] CompressedQueryJson, string QueryAssemblyVersion` | Yes |
| `SerializableQueryResult` | `string ResultTypeName, string QueryTypeName, byte[] CompressedResultJson, ...` | Yes |
| `SerializableListQueryResult` | Similar byte[]-based fields | Yes |
| `IProjectionState` metadata | `int SafeVersion, int UnsafeVersion, string? SafeLastSortableUniqueId, ...` | Yes (primitives only) |

---

## Proposed Interface Changes

### 1. `IProjectionRuntime` (Revised for WASM)

```csharp
public interface IProjectionRuntime
{
    // --- Metadata ---
    ResultBox<string> GetProjectorVersion(string projectorName);
    IReadOnlyList<string> GetAllProjectorNames();

    // --- State lifecycle ---
    ResultBox<IProjectionState> GenerateInitialState(string projectorName);
    ResultBox<byte[]> SerializeState(string projectorName, IProjectionState state);
    ResultBox<IProjectionState> DeserializeState(
        string projectorName, byte[] data, string safeWindowThreshold);

    // --- Event processing (CHANGED: SerializableEvent instead of Event) ---
    ResultBox<IProjectionState> ApplyEvent(
        string projectorName,
        IProjectionState currentState,
        SerializableEvent ev,            // ← Was: Event
        string safeWindowThreshold);

    ResultBox<IProjectionState> ApplyEvents(
        string projectorName,
        IProjectionState currentState,
        IReadOnlyList<SerializableEvent> events,  // ← Was: IReadOnlyList<Event>
        string safeWindowThreshold);

    // --- Buffered event promotion (NEW) ---
    ResultBox<IProjectionState> PromoteBufferedEvents(
        string projectorName,
        IProjectionState currentState,
        string safeWindowThreshold);

    // --- Query execution (CHANGED: no IServiceProvider) ---
    Task<ResultBox<SerializableQueryResult>> ExecuteQueryAsync(
        string projectorName,
        IProjectionState state,
        SerializableQueryParameter query);     // ← Removed: IServiceProvider

    Task<ResultBox<SerializableListQueryResult>> ExecuteListQueryAsync(
        string projectorName,
        IProjectionState state,
        SerializableQueryParameter query);     // ← Removed: IServiceProvider

    // --- Query routing (CHANGED: from SerializableQueryParameter) ---
    ResultBox<string> ResolveProjectorName(
        SerializableQueryParameter query);     // ← Was: IQueryCommon / IListQueryCommon

    // --- Snapshot envelope (NEW: replaces Grain-level JSON serialization) ---
    ResultBox<byte[]> SerializeSnapshot(
        string projectorName,
        IProjectionState state,
        bool isSafeState);

    ResultBox<IProjectionState> DeserializeSnapshot(
        string projectorName,
        byte[] snapshotData,
        string safeWindowThreshold);

    // --- Size estimation (NEW: replaces Grain's EstimatePayloadSizeBytesAsync) ---
    long EstimateStateSizeBytes(
        string projectorName,
        IProjectionState state);
}
```

**Key changes from current interface:**

| Change | Reason |
|--------|--------|
| `Event` → `SerializableEvent` | `Event.Payload` is `IEventPayload` (C# object). `SerializableEvent.Payload` is `byte[]`. WASM receives bytes and deserializes internally. |
| Remove `IServiceProvider` from query methods | .NET DI does not exist in WASM. Native implementation resolves services internally via captured reference. WASM implementation handles dependencies within the WASM module. |
| `ResolveProjectorName(IQueryCommon)` → `ResolveProjectorName(SerializableQueryParameter)` | `IQueryCommon` is a deserialized C# interface. `SerializableQueryParameter` contains the type name as string, sufficient for routing. |
| Add `PromoteBufferedEvents` | Safe/unsafe buffering management needs to be controllable from the orchestration layer but executed inside the runtime. |
| Add `SerializeSnapshot` / `DeserializeSnapshot` | Grain currently uses `JsonSerializer.Serialize(envelope, _domainTypes.JsonSerializerOptions)`. This must move into the runtime. |
| Add `EstimateStateSizeBytes` | Grain currently uses `_domainTypes.JsonSerializerOptions` for size estimation. |

### 2. `IProjectionState` (Revised for WASM)

```csharp
public interface IProjectionState
{
    // Metadata only — no object? payloads exposed
    int SafeVersion { get; }
    int UnsafeVersion { get; }
    string? SafeLastSortableUniqueId { get; }
    string? LastSortableUniqueId { get; }
    Guid? LastEventId { get; }

    // REMOVED: object? GetSafePayload()
    // REMOVED: object? GetUnsafePayload()
    // REMOVED: long EstimatePayloadSizeBytes(JsonSerializerOptions? options)
}
```

**Why remove payload access?**

The Grain currently accesses payloads for two reasons:
1. Query execution: `projectorProvider = () => Task.FromResult(ResultBox.FromValue(state.Payload!))`
2. Snapshot serialization

Both are now handled inside `IProjectionRuntime`:
- Query execution: `IProjectionRuntime.ExecuteQueryAsync` takes `IProjectionState` and handles payload access internally
- Snapshot: `IProjectionRuntime.SerializeSnapshot` takes `IProjectionState` and produces bytes

The Grain never needs to touch the payload directly. `IProjectionState` becomes an **opaque handle** — the native implementation holds C# objects internally, the WASM implementation holds a reference to WASM memory.

### 3. `IEventRuntime` (No change needed)

```csharp
public interface IEventRuntime
{
    string SerializeEventPayload(IEventPayload payload);
    IEventPayload? DeserializeEventPayload(string eventTypeName, string json);
    Type? GetEventType(string eventTypeName);
}
```

**Note**: `IEventRuntime` is used in the Grain's stream handler to convert `SerializableEvent` → `Event`. With the new design where `IProjectionRuntime.ApplyEvent` accepts `SerializableEvent`, the Grain no longer needs `IEventRuntime` at all for projection purposes. `IEventRuntime` may still be needed for event writing/command handling but can potentially be removed from Grain dependencies.

### 4. `ITagProjectionRuntime` (Minor change)

```csharp
public interface ITagProjectionRuntime
{
    ResultBox<ITagProjector> GetProjector(string tagProjectorName);
    ResultBox<string> GetProjectorVersion(string tagProjectorName);
    IReadOnlyList<string> GetAllProjectorNames();
    string? TryGetProjectorForTagGroup(string tagGroupName);
    ITag ResolveTag(string tagString);
    ResultBox<byte[]> SerializePayload(ITagStatePayload payload);
    ResultBox<ITagStatePayload> DeserializePayload(string payloadName, byte[] data);
}
```

Note: `ITag` and `ITagStatePayload` are interfaces, not concrete classes. If WASM needs tag actors, these would need to be serializable too. For now, tag grains are likely native-only.

---

## Actor Layer Abstraction

### Problem

The Grain currently creates `GeneralMultiProjectionActor` directly:
```csharp
_projectionActor = new GeneralMultiProjectionActor(_domainTypes, projectorName, options, logger);
```

`GeneralMultiProjectionActor` is a concrete C# class in `Sekiban.Dcb.Core` with 600+ lines that:
- Manages safe/unsafe state via `IDualStateAccessor` / reflection
- Handles event buffering and safe window
- Builds snapshot envelopes using `DcbDomainTypes`
- Deserializes `SerializableEvent` using `_domain.EventTypes`

For WASM, a completely different actor implementation would exist.

### Solution: `IProjectionActorHost`

Introduce an abstract actor interface that the Grain talks to. This replaces direct `GeneralMultiProjectionActor` usage.

```csharp
/// <summary>
///     Abstraction over the projection actor that manages safe/unsafe state.
///     Native implementation wraps GeneralMultiProjectionActor.
///     WASM implementation delegates to the WASM projection engine.
/// </summary>
public interface IProjectionActorHost
{
    // --- Event processing ---
    Task AddSerializableEventsAsync(
        IReadOnlyList<SerializableEvent> events,
        bool finishedCatchUp = true,
        EventSource source = EventSource.Unknown);

    // --- State access (metadata only) ---
    Task<ResultBox<ProjectionStateMetadata>> GetStateMetadataAsync(
        bool canGetUnsafeState = true);

    // --- Snapshot management (bytes in/out) ---
    Task<ResultBox<byte[]>> GetSnapshotBytesAsync(
        bool canGetUnsafeState = true);

    Task RestoreSnapshotAsync(byte[] snapshotData, CancellationToken ct = default);

    // --- Query execution ---
    Task<ResultBox<SerializableQueryResult>> ExecuteQueryAsync(
        SerializableQueryParameter query);

    Task<ResultBox<SerializableListQueryResult>> ExecuteListQueryAsync(
        SerializableQueryParameter query);

    // --- Orchestration control ---
    void ForcePromoteBufferedEvents();
    Task<string> GetSafeLastSortableUniqueIdAsync();
    string PeekCurrentSafeWindowThresholdValue();
    Task<bool> IsSortableUniqueIdReceived(string sortableUniqueId);
    long EstimateStateSizeBytes();
}
```

**`ProjectionStateMetadata`** — WASM-safe metadata record:

```csharp
public record ProjectionStateMetadata(
    string ProjectorName,
    string ProjectorVersion,
    string LastSortableUniqueId,
    Guid LastEventId,
    int Version,
    bool IsCatchedUp,
    bool IsSafeState,
    int? SafeVersion = null,
    string? SafeLastSortableUniqueId = null);
```

### `NativeProjectionActorHost`

Wraps `GeneralMultiProjectionActor` for native C# execution:

```csharp
public class NativeProjectionActorHost : IProjectionActorHost
{
    private readonly GeneralMultiProjectionActor _actor;
    private readonly IProjectionRuntime _runtime;

    public NativeProjectionActorHost(
        IProjectionRuntime runtime,
        string projectorName,
        GeneralMultiProjectionActorOptions? options,
        ILogger? logger)
    {
        // The native runtime has access to DcbDomainTypes internally
        // Extract it for actor construction (internal detail)
        _runtime = runtime;
        _actor = CreateActorFromRuntime(runtime, projectorName, options, logger);
    }

    // All methods delegate to _actor, converting between
    // IProjectionRuntime types and actor-internal types
}
```

### `IProjectionActorHostFactory`

```csharp
public interface IProjectionActorHostFactory
{
    IProjectionActorHost Create(
        string projectorName,
        GeneralMultiProjectionActorOptions? options = null,
        ILogger? logger = null);
}
```

Native implementation:
```csharp
public class NativeProjectionActorHostFactory : IProjectionActorHostFactory
{
    private readonly DcbDomainTypes _domainTypes;

    public NativeProjectionActorHostFactory(DcbDomainTypes domainTypes)
    {
        _domainTypes = domainTypes;
    }

    public IProjectionActorHost Create(
        string projectorName,
        GeneralMultiProjectionActorOptions? options = null,
        ILogger? logger = null)
    {
        return new NativeProjectionActorHost(_domainTypes, projectorName, options, logger);
    }
}
```

---

## Tag Actor Abstraction

### `ITagActorFactory`

```csharp
public interface ITagActorFactory
{
    GeneralTagStateActor CreateTagStateActor(
        string tagStateId,
        IEventStore eventStore,
        TagStateOptions options,
        IActorObjectAccessor actorAccessor,
        ITagStatePersistent statePersistent);

    GeneralTagConsistentActor CreateTagConsistentActor(
        string tagName,
        IEventStore? eventStore,
        TagConsistentActorOptions options);
}
```

For WASM support of tag actors (future), these return types would need to become interfaces too. For now, tag actors are native-only.

---

## MultiProjectionGrain Migration

### Before (current):

```csharp
public class MultiProjectionGrain : Grain, IMultiProjectionGrain
{
    private readonly DcbDomainTypes _domainTypes;        // ← REMOVE
    private readonly IEventRuntime _eventRuntime;        // ← REMOVE (no longer needed)
    private GeneralMultiProjectionActor? _projectionActor;  // ← Change type

    public MultiProjectionGrain(
        ...,
        DcbDomainTypes domainTypes,                      // ← REMOVE
        IEventRuntime eventRuntime,                      // ← REMOVE
        ...)
    {
        _domainTypes = domainTypes;
        _eventRuntime = eventRuntime;
    }
}
```

### After:

```csharp
public class MultiProjectionGrain : Grain, IMultiProjectionGrain
{
    private readonly IProjectionRuntime _projectionRuntime;   // ← NEW
    private readonly IProjectionActorHostFactory _actorFactory; // ← NEW
    private IProjectionActorHost? _projectionActor;           // ← Changed type

    public MultiProjectionGrain(
        ...,
        IProjectionRuntime projectionRuntime,
        IProjectionActorHostFactory actorFactory,
        ...)
    {
        _projectionRuntime = projectionRuntime;
        _actorFactory = actorFactory;
    }
}
```

### Usage migration (all ~20 `_domainTypes` occurrences):

| # | Current code | New code | Category |
|---|-------------|----------|----------|
| 1 | `new GeneralMultiProjectionActor(_domainTypes, ...)` | `_actorFactory.Create(projectorName, ...)` | Actor creation |
| 2 | `_domainTypes.MultiProjectorTypes.GetProjectorVersion(name)` | `_projectionRuntime.GetProjectorVersion(name)` | Metadata |
| 3 | `queryParameter.ToQueryAsync(_domainTypes)` | Eliminated — `_projectionActor.ExecuteQueryAsync(query)` handles internally | Query |
| 4 | `_domainTypes.QueryTypes.ExecuteQueryAsync(query, provider, ...)` | `_projectionActor.ExecuteQueryAsync(query)` | Query |
| 5 | `_domainTypes.QueryTypes.ExecuteListQueryAsGeneralAsync(...)` | `_projectionActor.ExecuteListQueryAsync(query)` | Query |
| 6 | `SerializableQueryResult.CreateFromAsync(result, _domainTypes.JsonSerializerOptions)` | Eliminated — actor returns `SerializableQueryResult` directly | Query |
| 7 | `SerializableListQueryResult.CreateFromAsync(result, _domainTypes.JsonSerializerOptions)` | Eliminated — actor returns `SerializableListQueryResult` directly | Query |
| 8 | `JsonSerializer.Serialize(dto, _domainTypes.JsonSerializerOptions)` (size estimation) | `_projectionActor.EstimateStateSizeBytes()` | Size |
| 9 | `JsonSerializer.SerializeAsync(envelope, _domainTypes.JsonSerializerOptions)` (persist) | `_projectionActor.GetSnapshotBytesAsync()` | Snapshot |
| 10 | `JsonSerializer.Deserialize<...>(json, _domainTypes.JsonSerializerOptions)` (restore) | `_projectionActor.RestoreSnapshotAsync(bytes)` | Snapshot |
| 11 | `JsonSerializer.Serialize(rb.GetValue(), _domainTypes.JsonSerializerOptions)` (GetSnapshotJson) | `_projectionActor.GetSnapshotBytesAsync()` + UTF8 encode | Snapshot |
| 12 | Stream event deserialization using `_eventRuntime` | Pass `SerializableEvent` directly to `_projectionActor.AddSerializableEventsAsync()` | Events |

### Query execution simplification

Current Grain code (~100 lines):
```csharp
// 1. Deserialize query
var queryBox = await queryParameter.ToQueryAsync(_domainTypes);
var query = (IQueryCommon)queryBox.GetValue();
// 2. Get state from actor
var stateResult = await _projectionActor.GetStateAsync();
var projectorProvider = () => Task.FromResult(ResultBox.FromValue(stateResult.GetValue().Payload!));
// 3. Execute query
var result = await _domainTypes.QueryTypes.ExecuteQueryAsync(query, projectorProvider, ServiceProvider, ...);
// 4. Serialize result
return await SerializableQueryResult.CreateFromAsync(new QueryResultGeneral(value, resultType, query), _domainTypes.JsonSerializerOptions);
```

New Grain code (~5 lines):
```csharp
await EnsureInitializedAsync();
await StartSubscriptionAsync();
if (_projectionActor == null) return SerializableQueryResult.Empty;
return await _projectionActor.ExecuteQueryAsync(queryParameter);
```

The `NativeProjectionActorHost` handles all the internal complexity (deserialize query, get payload, execute, serialize result) using `DcbDomainTypes` internally.

---

## IServiceProvider Handling

### Problem
Current `IProjectionRuntime.ExecuteQueryAsync` takes `IServiceProvider` for DI resolution in query handlers. WASM cannot use .NET DI.

### Solution
Move `IServiceProvider` into the native implementation:

```csharp
public class NativeProjectionActorHost : IProjectionActorHost
{
    // IServiceProvider is captured at construction time from the Grain's context
    private readonly IServiceProvider _serviceProvider;

    public NativeProjectionActorHost(
        DcbDomainTypes domainTypes,
        string projectorName,
        IServiceProvider serviceProvider,  // ← From Grain
        ...)
    {
        _serviceProvider = serviceProvider;
    }

    public Task<ResultBox<SerializableQueryResult>> ExecuteQueryAsync(
        SerializableQueryParameter query)
    {
        // Uses _serviceProvider internally
        return _runtime.ExecuteQueryAsync(projectorName, state, query, _serviceProvider);
    }
}
```

The `IProjectionActorHostFactory.Create` method receives `IServiceProvider` from the Grain:
```csharp
public interface IProjectionActorHostFactory
{
    IProjectionActorHost Create(
        string projectorName,
        IServiceProvider serviceProvider,      // ← Grain passes this
        GeneralMultiProjectionActorOptions? options = null,
        ILogger? logger = null);
}
```

For WASM, the factory implementation would handle service resolution differently (e.g., WASM module has its own service registry).

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

The state handle is an opaque `i32` (index into WASM-side state table). The .NET side wraps it in `WasmProjectionState : IProjectionState` that stores the handle and fetches metadata from WASM when needed.

---

## Implementation Phases

### Phase 3A: Interface Changes + Actor Abstraction

1. Revise `IProjectionRuntime`:
   - `Event` → `SerializableEvent`
   - Remove `IServiceProvider` from query methods
   - `ResolveProjectorName(IQueryCommon)` → `ResolveProjectorName(SerializableQueryParameter)`
   - Add `PromoteBufferedEvents`, `SerializeSnapshot`, `DeserializeSnapshot`, `EstimateStateSizeBytes`
2. Revise `IProjectionState`: remove `GetSafePayload()`, `GetUnsafePayload()`, `EstimatePayloadSizeBytes()`
3. Update `NativeProjectionRuntime` + `NativeProjectionState` for new signatures
4. Create `IProjectionActorHost` interface
5. Create `NativeProjectionActorHost` (wraps `GeneralMultiProjectionActor`)
6. Create `IProjectionActorHostFactory` + `NativeProjectionActorHostFactory`
7. Create `ITagActorFactory` + `NativeTagActorFactory`

### Phase 3B: Grain Migration

1. Remove `DcbDomainTypes` from `MultiProjectionGrain` constructor
2. Remove `IEventRuntime` from `MultiProjectionGrain` (no longer needed)
3. Add `IProjectionRuntime` + `IProjectionActorHostFactory` to `MultiProjectionGrain`
4. Replace `GeneralMultiProjectionActor?` with `IProjectionActorHost?`
5. Migrate all ~20 `_domainTypes` usages (see migration table above)
6. Remove `DcbDomainTypes` from `TagStateGrain` + `TagConsistentGrain`
7. Update DI registrations in all host `Program.cs` files
8. Update test projects

### Phase 3C: Actor Internals (Future, enables WASM)

1. Refactor `GeneralMultiProjectionActor` to use `IProjectionRuntime` instead of `DcbDomainTypes`
2. Remove `_domain` field from `GeneralMultiProjectionActor`
3. Refactor `IDualStateAccessor.ProcessEventAs/PromoteBufferedEvents` to not take `DcbDomainTypes`
4. Refactor `GeneralTagStateActor` to use `ITagProjectionRuntime`
5. Refactor `GeneralTagConsistentActor` to use `ITagProjectionRuntime`

---

## Files to Modify/Create

### New files:
1. `Runtimes/IProjectionActorHost.cs` — Actor abstraction interface
2. `Runtimes/IProjectionActorHostFactory.cs` — Factory interface
3. `Runtimes/NativeProjectionActorHost.cs` — Native implementation wrapping GeneralMultiProjectionActor
4. `Runtimes/NativeProjectionActorHostFactory.cs` — Native factory
5. `Runtimes/ProjectionStateMetadata.cs` — WASM-safe state metadata record
6. `Runtimes/ITagActorFactory.cs` — Tag actor factory
7. `Runtimes/NativeTagActorFactory.cs` — Native tag actor factory

### Modified files:
1. `Runtimes/IProjectionRuntime.cs` — Revised interface (SerializableEvent, remove IServiceProvider, etc.)
2. `Runtimes/IProjectionState.cs` — Remove payload accessors
3. `Runtimes/NativeProjectionRuntime.cs` — Implement revised interface
4. `Runtimes/NativeProjectionState.cs` — Remove payload exposure
5. `Grains/MultiProjectionGrain.cs` — Replace _domainTypes with runtime interfaces
6. `Grains/TagStateGrain.cs` — Replace _domainTypes with factory
7. `Grains/TagConsistentGrain.cs` — Replace _domainTypes with factory
8. Host `Program.cs` files (3+) — DI registration updates
9. Test files — DI registration updates

---

## Risk Assessment

| Risk | Severity | Mitigation |
|------|----------|------------|
| `NativeProjectionActorHost` is a large adapter between `IProjectionActorHost` and `GeneralMultiProjectionActor` | High | Incremental: first wrap actor 1:1, then optimize |
| `IProjectionRuntime.ExecuteQueryAsync` without `IServiceProvider` requires capturing it at actor creation | Medium | Factory pattern provides `IServiceProvider` from Grain context |
| Removing `IProjectionState.GetSafePayload()/GetUnsafePayload()` may break existing consumers | Medium | Check all consumers — only Grain query execution uses it, which is being refactored |
| `GeneralMultiProjectionActor` stays dependent on `DcbDomainTypes` in Phase 3A/3B | Low | Acceptable — hidden behind `NativeProjectionActorHost`. Phase 3C addresses this |
| Large number of files affected | Medium | Phase 3B can be done incrementally (one method at a time) |

---

## Summary of Interface Boundaries

```
                     WASM-safe boundary
                     (only primitives, bytes, strings)
                           │
  Grain ──→ IProjectionActorHost ──→ [NativeProjectionActorHost]
                           │                    │
                           │              GeneralMultiProjectionActor
                           │                    │
                           │              DcbDomainTypes (internal)
                           │
            IProjectionRuntime ──→ [NativeProjectionRuntime]
                           │                    │
                           │              DcbDomainTypes (internal)
                           │
                      [Future: WasmProjectionActorHost]
                           │
                      [WASM module via wasm-bindgen]
```

The Grain layer sees ONLY:
- `IProjectionActorHost` (event processing, queries, snapshots)
- `IProjectionRuntime` (metadata, projector routing)
- Primitive types (`string`, `int`, `byte[]`, `Guid`)
- Serializable DTOs (`SerializableEvent`, `SerializableQueryParameter`, `SerializableQueryResult`)

No `DcbDomainTypes`, no `JsonSerializerOptions`, no `IServiceProvider`, no `IMultiProjectionPayload`.
