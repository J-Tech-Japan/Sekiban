## Goal
Introduce a serialized command executor contract in Sekiban so WASM local/remote command models can use the same server-side commit semantics without relying on typed `ISekibanExecutor` paths.

## Why this is needed
Current Sekiban command execution is typed-first (`ISekibanExecutor`, `ICommandContext`).
For WASM command execution, we need a boundary where:
- tag state is fetched as `SerializableTagState`,
- command output events are accepted as serialized payload candidates,
- server generates event metadata and applies consistency reservation before write.

Without this, SekibanWasmRuntime must duplicate commit semantics outside Sekiban.

## Scope (Sekiban side only)
- Add serialized command executor interface + DTOs in Core.
- Add Orleans implementation with reservation/write/confirm/cancel flow.
- Register via DI (additive).
- Add tests and docs.

## References
- `tasks/0220_serialized_command_wasm_contract/design.md`
- `tasks/0220_serialized_command_wasm_contract/implementation-plan.md`
- `dcb/src/Sekiban.Dcb.Core/Storage/IEventStore.cs`
- `dcb/src/Sekiban.Dcb.Core/Actors/CoreGeneralSekibanExecutor.cs`

## Task Checklist
### Phase 1: Contract
- [ ] Add `ISerializedSekibanDcbExecutor`
- [ ] Add `SerializedCommitRequest`, `SerializableEventCandidate`, `SerializedCommitResult`

### Phase 2: Orleans implementation
- [ ] Implement `GetTagStateAsync` (serialized return)
- [ ] Implement `CommitSerializableEventsAsync` with consistency reservation flow

### Phase 3: Wiring
- [ ] Register implementation in Orleans DI extensions
- [ ] Keep existing typed executor registrations untouched

### Phase 4: Tests
- [ ] success path (single event)
- [ ] success path (multi event)
- [ ] conflict path (mismatch)
- [ ] rollback/cancel path

### Phase 5: Docs
- [ ] Add concise docs for purpose and intended WASM integration usage

## Success Criteria
- Serialized command executor works end-to-end in Orleans runtime.
- Existing `ISekibanExecutor` behavior remains unchanged.
- Contract is enough for SekibanWasmRuntime to implement both local and remote WASM command paths without additional Sekiban API changes.

## Non-goals
- Implement full WASM runtime in Sekiban.
- Replace typed command APIs.
