## Goal
Introduce a runtime-swappable MultiProjection abstraction in Sekiban so `MultiProjectionGrain` can be cleanly separated for Native and WASM runtime implementations.

## Background
TagState path now has a cleaner serialized abstraction boundary. MultiProjection path still has native-oriented coupling and is harder to replace with a WASM implementation.

This issue is the Sekiban-side prerequisite before implementing the WASM counterpart in SekibanWasmRuntime.

## Scope (Sekiban side)
- Design and introduce MultiProjection primitive abstraction in Core.
- Add native implementation adapter (Orleans runtime side).
- Refactor `MultiProjectionGrain` to use abstraction via DI.
- Keep existing public behavior unchanged.

## References
- `tasks/0216_multiprojection_wasm_abstraction/design.md`
- `dcb/src/Sekiban.Dcb.Orleans.Core/Grains/MultiProjectionGrain.cs`
- `dcb/src/Sekiban.Dcb.Core/Actors/GeneralMultiProjectionActor.cs`
- `dcb/src/Sekiban.Dcb.Orleans.Core/SekibanDcbNativeRuntimeExtensions.cs`

## Proposed Task Breakdown
### Phase 1: Abstraction definition
- [ ] Add `IMultiProjectionProjectionPrimitive` and `IMultiProjectionProjectionAccumulator` in Core runtime layer.
- [ ] Define serialized input/output contract and metadata contract.

### Phase 2: Native adapter
- [ ] Implement native adapter using existing GeneralMultiProjectionActor semantics.
- [ ] Register adapter in native runtime DI extensions.

### Phase 3: Grain migration
- [ ] Update `MultiProjectionGrain` to call abstraction instead of native-specific transition logic.
- [ ] Preserve catch-up, persistence, and health/status behavior.

### Phase 4: Validation
- [ ] Add/adjust parity tests for init, catch-up, restore, mismatch handling.
- [ ] Ensure existing integration tests continue to pass.

## Success Criteria
- `MultiProjectionGrain` runtime transition behavior is abstraction-based and DI-swappable.
- Native behavior parity maintained.
- SekibanWasmRuntime can implement same contract without changing Orleans grain API.

## Non-goals
- Implement full WASM runtime in this issue.
- Change public executor/grain API semantics.

## Risks
- Safe/unsafe dual-state behavior regression.
- Catch-up/stream ordering regression.
- Snapshot compatibility breakage.
