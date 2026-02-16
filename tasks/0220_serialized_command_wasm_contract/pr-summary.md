# PR Summary (Serialized Command Contract for WASM)

## What this PR contains
- Self-contained Sekiban-side design for serialized command contract.
- Implementation plan that can be executed without external design references.
- Issue body template with phased checklist and success criteria.

## Deliverables
- `tasks/0220_serialized_command_wasm_contract/design.md`
- `tasks/0220_serialized_command_wasm_contract/implementation-plan.md`
- `tasks/0220_serialized_command_wasm_contract/issue.md`
- `tasks/0220_serialized_command_wasm_contract/pr-summary.md`

## Why now
This is a prerequisite for SekibanWasmRuntime to implement PoC-equivalent local/remote WASM command execution while keeping Sekiban typed APIs stable.

## Next step
- Open/track Sekiban implementation issue from `issue.md`.
- Execute phased implementation in small PR slices.
