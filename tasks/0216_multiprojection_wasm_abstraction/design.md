# MultiProjectionGrain WASM/Native Split Design (Sekiban side)

## Context
TagState path has been moved to a clear serialized boundary and is now configurable enough to swap runtime behavior. MultiProjection path is still tightly coupled to `GeneralMultiProjectionActor` behavior and native assumptions inside `MultiProjectionGrain`.

Goal is to make MultiProjection follow the same direction:
- infrastructure grain stays thin
- projection execution details are runtime-swappable
- WASM and Native can be separated without changing public API of Sekiban executors.

## Target Outcome
`MultiProjectionGrain` should depend on a minimal runtime abstraction that can be implemented by:
- native in-process multi-projector runtime
- wasm-hosted multi-projector runtime (implemented in SekibanWasmRuntime)

This allows selecting runtime by DI, while keeping:
- current Orleans grain contract
- current query/executor contract
- current persistence behavior.

## Non-goals
- This phase does not implement full WASM runtime in Sekiban.
- This phase does not introduce Orleans-specific wasm grain types.
- This phase does not change external API behavior.

## Current Bottlenecks
1. `MultiProjectionGrain` owns too much behavior (catch-up, buffering, state/snapshot flows, host lifecycle).
2. Host abstraction (`IProjectionActorHost`) is still effectively native-oriented for multi-projection state transitions.
3. No serialized primitive contract equivalent to TagState accumulator for MultiProjection.
4. Runtime selection is not explicit for MultiProjection path.

## Design Principles
1. Keep Orleans grain as infrastructure orchestrator only.
2. Move projection state transition semantics behind runtime abstraction.
3. Use serialized boundary (`SerializableEvent`, serialized snapshot payloads) as canonical runtime input/output.
4. Keep backward compatibility for existing native runtime.
5. Prefer additive changes first; remove native-only paths only after parity tests.

## Proposed Abstraction Layers

### A) Core runtime abstraction in Sekiban.Dcb.Core
Add a new MultiProjection primitive contract designed for runtime swapping.

Proposed interface shape (draft):

```csharp
public interface IMultiProjectionProjectionPrimitive
{
    IMultiProjectionProjectionAccumulator CreateAccumulator(
        string projectorName,
        string projectorVersion);
}

public interface IMultiProjectionProjectionAccumulator
{
    bool ApplySnapshot(SerializableMultiProjectionStateEnvelope? snapshot);
    bool ApplyEvents(
        IReadOnlyList<SerializableEvent> events,
        string? latestSortableUniqueId,
        CancellationToken cancellationToken = default);
    ResultBox<SerializableMultiProjectionStateEnvelope> GetSnapshot();
    ResultBox<MultiProjectionStateMetadata> GetMetadata();
}
```

Notes:
- Keep this independent from Orleans.
- Keep runtime-agnostic payload shape.
- Metadata includes safe/unsafe progress needed by grain persistence and status APIs.

### B) Native implementation in Orleans runtime
Add native implementation that wraps current `GeneralMultiProjectionActor` semantics:
- state restore
- event apply (ordered, incremental)
- snapshot build
- safe/unsafe metadata extraction.

`MultiProjectionGrain` should not instantiate concrete actor logic directly.

### C) Grain dependency switch
`MultiProjectionGrain` should resolve runtime primitive via DI.
No direct `new` of native runtime-specific helpers.

## MultiProjectionGrain Refactor Plan

### Step 1: Introduce seam without behavior change
- Add primitive interfaces and native adapter.
- Keep existing flow but route state transition calls through adapter.
- Preserve current tests.

### Step 2: Reduce grain-owned business logic
- Move event ordering and transition details into primitive.
- Keep grain responsibilities:
  - stream subscription
  - catch-up scheduling
  - persistence orchestration
  - health/status endpoints

### Step 3: Snapshot/state path normalization
- Consolidate snapshot read/write through primitive contract.
- Ensure projector-version mismatch behavior is explicit.
- Keep current integrity guard semantics.

### Step 4: Runtime registration explicitness
- Add explicit service registration method for MultiProjection runtime primitive.
- Native is default.
- WASM runtime package can override registration later.

## Acceptance Criteria
1. `MultiProjectionGrain` no longer depends on native concrete transition logic directly.
2. All state transitions go through primitive abstraction.
3. Existing native behavior and tests are preserved.
4. New abstraction supports out-of-process/WASM implementation without Orleans contract changes.
5. Clear DI registration point exists for runtime replacement.

## Required Tests (Sekiban side)
1. Native parity tests for existing scenarios:
- initialization
- catch-up incremental apply
- snapshot restore same projector version
- snapshot restore projector version mismatch handling
- persistence metadata consistency (safe/unsafe versions)

2. Grain integration tests:
- stream + catch-up path still works
- status and health API values remain stable
- no regression in persisted state checkpoint behavior

## Implementation Slices (small PR sequence)
1. `Slice-1`: interface + native adapter + wiring (no logic move)
2. `Slice-2`: grain routes event/snapshot transitions through adapter
3. `Slice-3`: cleanup direct native-only calls from grain
4. `Slice-4`: add parity tests and update docs

## Risks
1. Safe/unsafe dual state semantics are subtle; accidental flattening can break consistency guarantees.
2. Catch-up overlap and stream dedupe behaviors must remain deterministic.
3. Snapshot compatibility across versions must stay backward-compatible.

## Open Questions
1. Should safe/unsafe buffer promotion policy remain entirely in native adapter, or partially in grain orchestration?
2. Should projector-version mismatch reset be handled by primitive or by grain before primitive call?
3. Do we need separate primitive for query execution, or keep query path on existing `IProjectionRuntime` for now?

## Follow-up (after Sekiban side)
Once this abstraction is merged in Sekiban:
- implement WASM counterpart in SekibanWasmRuntime
- wire DI override there
- add cross-runtime parity tests (native vs wasm snapshot/event sequences)
