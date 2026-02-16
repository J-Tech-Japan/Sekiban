# Implementation Plan (Self-contained)

This plan is written so implementation can start without referencing any external design document.

## Phase 0: Baseline lock
- [ ] Record Sekiban SHA in issue comment.
- [ ] Confirm current files:
  - `dcb/src/Sekiban.Dcb.Core/Storage/IEventStore.cs`
  - `dcb/src/Sekiban.Dcb.Core/Events/SerializableEvent.cs`
  - `dcb/src/Sekiban.Dcb.Core/Tags/SerializableTagState.cs`
  - `dcb/src/Sekiban.Dcb.Core/Actors/CoreGeneralSekibanExecutor.cs`

## Phase 1: Core contract addition
- [ ] Add new interface in Core runtime layer:
  - `ISerializedSekibanDcbExecutor`
- [ ] Add DTOs:
  - `SerializedCommitRequest`
  - `SerializableEventCandidate`
  - `SerializedCommitResult`
- [ ] Add XML docs including conflict and rollback semantics.

Done when:
- contract compiles,
- no existing API removed/changed.

## Phase 2: Orleans executor implementation
- [ ] Add Orleans implementation class (example name: `OrleansSerializedSekibanDcbExecutor`).
- [ ] `GetTagStateAsync`:
  - resolve existing TagState actor path,
  - return `SerializableTagState` directly.
- [ ] `CommitSerializableEventsAsync`:
  - generate event ids and sortable ids server-side,
  - apply consistency reservation by expected sortable-id,
  - call `IEventStore.WriteSerializableEventsAsync`,
  - confirm reservations on success,
  - cancel reservations on failure.

Done when:
- success and failure paths are deterministic and testable.

## Phase 3: DI registration
- [ ] Register serialized executor in Orleans runtime extension.
- [ ] Keep existing `ISekibanExecutor` registration unchanged.

Done when:
- serialized executor is resolvable from DI,
- existing applications continue working with no code change.

## Phase 4: Tests
- [ ] Add unit tests for executor behavior:
  - commit success (single event)
  - commit success (multi event)
  - mismatch conflict
  - rollback/cancel on write failure
- [ ] Add integration-level test if needed for reservation semantics.

Done when:
- all new tests pass,
- existing dcb tests pass.

## Phase 5: Documentation
- [ ] Add short README section in relevant dcb docs about serialized executor purpose.
- [ ] Include usage snippet for WASM runtime integration (in-proc and HTTP adapter use cases).

## Minimum command set before merge
```bash
dotnet build Sekiban.sln
dotnet test Sekiban.sln
```

If full solution is too heavy, at minimum run targeted dcb test projects and attach exact commands/results in PR.

## Definition of done
- Contract + implementation + tests are merged.
- No regression in existing typed command execution path.
- SekibanWasmRuntime can start implementation against this contract without new Sekiban-side API changes.
