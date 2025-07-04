# TypeScript Type Registry Implementation Progress

## Overview

This document tracks the implementation progress of the Zod-based schema-first type registry system for Sekiban TypeScript. The implementation follows t-wada's TDD style with comprehensive tests written before implementation.

## Current Status

- **Design Phase**: ✅ Complete (see type-registry.md)
- **Implementation Phase**: 🚧 Starting
- **Migration Phase**: ⏳ Not started

## Implementation Approach

Following t-wada's TDD principles:
1. **One assertion per test** - Each test validates exactly one behavior
2. **Clear test names** - Test names describe the expected behavior
3. **AAA pattern** - Arrange, Act, Assert structure
4. **Test first** - Write failing tests before implementation
5. **Small steps** - Implement just enough to make tests pass

## Phase 1: Core Schema System Tests (Week 1)

### Event Schema Tests
Location: `packages/core/src/schema-registry/tests/event-schema.test.ts`

- [ ] Test: `defineEvent creates event definition with type property`
- [ ] Test: `defineEvent schema validates correct data`
- [ ] Test: `defineEvent schema rejects invalid data`
- [ ] Test: `defineEvent create function adds type discriminator`
- [ ] Test: `defineEvent parse function validates and adds type`
- [ ] Test: `defineEvent safeParse returns success for valid data`
- [ ] Test: `defineEvent safeParse returns error for invalid data`
- [ ] Test: `event type inference works correctly`

### Command Schema Tests
Location: `packages/core/src/schema-registry/tests/command-schema.test.ts`

- [ ] Test: `defineCommand creates command definition with type property`
- [ ] Test: `defineCommand schema validates correct data`
- [ ] Test: `defineCommand handlers.specifyPartitionKeys returns correct keys`
- [ ] Test: `defineCommand handlers.validate performs business validation`
- [ ] Test: `defineCommand handlers.handle returns events for valid aggregate`
- [ ] Test: `defineCommand create function adds commandType property`
- [ ] Test: `defineCommand validate combines schema and business validation`
- [ ] Test: `defineCommand execute calls handler with typed data`

### Projector Schema Tests
Location: `packages/core/src/schema-registry/tests/projector-schema.test.ts`

- [ ] Test: `defineProjector creates projector with aggregateType`
- [ ] Test: `defineProjector getInitialState returns empty aggregate`
- [ ] Test: `defineProjector project handles registered event types`
- [ ] Test: `defineProjector project ignores unregistered event types`
- [ ] Test: `defineProjector projections transform state correctly`
- [ ] Test: `defineProjector handles state type transitions`
- [ ] Test: `defineProjector project increments version`
- [ ] Test: `defineProjector project handles projection errors`

### Schema Registry Tests
Location: `packages/core/src/schema-registry/tests/registry.test.ts`

- [ ] Test: `SchemaRegistry registerEvent stores event schema`
- [ ] Test: `SchemaRegistry registerCommand stores command definition`
- [ ] Test: `SchemaRegistry registerProjector stores projector definition`
- [ ] Test: `SchemaRegistry deserializeEvent validates with schema`
- [ ] Test: `SchemaRegistry deserializeEvent throws for unknown type`
- [ ] Test: `SchemaRegistry getCommand returns registered command`
- [ ] Test: `SchemaRegistry getProjector returns registered projector`
- [ ] Test: `SchemaRegistry prevents duplicate registrations`

## Phase 2: Implementation (Week 1-2)

### Event Schema Implementation
- [ ] Create `EventSchemaDefinition` interface
- [ ] Implement `defineEvent` function
- [ ] Add type inference support
- [ ] Implement create/parse/safeParse methods

### Command Schema Implementation
- [ ] Create `CommandHandlers` interface
- [ ] Create `CommandSchemaDefinition` interface
- [ ] Implement `defineCommand` function
- [ ] Add handler integration

### Projector Schema Implementation
- [ ] Create `ProjectorDefinition` interface
- [ ] Implement `defineProjector` function
- [ ] Add projection handlers
- [ ] Support state transitions

### Registry Implementation
- [ ] Create `SchemaRegistry` class
- [ ] Implement registration methods
- [ ] Add lookup methods
- [ ] Create global instance

## Phase 3: Code Generation (Week 2)

### Scanner Tests
Location: `packages/codegen/src/tests/schema-scanner.test.ts`

- [ ] Test: `SchemaScanner finds defineEvent calls`
- [ ] Test: `SchemaScanner extracts event type names`
- [ ] Test: `SchemaScanner finds defineCommand calls`
- [ ] Test: `SchemaScanner extracts command type names`
- [ ] Test: `SchemaScanner finds defineProjector calls`
- [ ] Test: `SchemaScanner handles multiple files`
- [ ] Test: `SchemaScanner ignores test files`

### Generator Tests
Location: `packages/codegen/src/tests/code-generator.test.ts`

- [ ] Test: `CodeGenerator creates import statements`
- [ ] Test: `CodeGenerator creates event registry object`
- [ ] Test: `CodeGenerator creates command registry object`
- [ ] Test: `CodeGenerator creates type exports`
- [ ] Test: `CodeGenerator creates union types`
- [ ] Test: `CodeGenerator handles empty registries`

### Implementation
- [ ] Create `@sekiban/codegen` package
- [ ] Implement ts-morph scanner
- [ ] Build code generator
- [ ] Create CLI tool
- [ ] Add watch mode support
- [ ] Create Vite plugin

## Phase 4: Migration Support (Week 3)

### Compatibility Tests
Location: `packages/core/src/schema-registry/tests/compatibility.test.ts`

- [ ] Test: `adaptClassToSchema converts class to schema definition`
- [ ] Test: `mixed registry supports both schemas and classes`
- [ ] Test: `executor works with schema-based commands`
- [ ] Test: `executor works with class-based commands`
- [ ] Test: `projector works with schema events and class projector`

### Implementation
- [ ] Create class-to-schema adapters
- [ ] Update executor for schema support
- [ ] Add backward compatibility layer
- [ ] Write migration guide
- [ ] Create codemods

## Key Decisions Made

### 1. Pure Functions Over Classes
- **Decision**: Use factory functions and configuration objects instead of classes
- **Rationale**: Better tree-shaking, simpler testing, aligns with modern TypeScript

### 2. Zod for Validation
- **Decision**: Use Zod schemas for all runtime validation
- **Rationale**: Already used in the project, excellent TypeScript inference, built-in validation

### 3. Explicit Registration
- **Decision**: Require explicit registration calls instead of decorator side-effects
- **Rationale**: More predictable, better for tree-shaking, easier to debug

### 4. Type-First Design
- **Decision**: Let TypeScript inference drive the API design
- **Rationale**: Better developer experience, catches errors at compile time

### 5. Gradual Migration
- **Decision**: Support both old and new patterns during transition
- **Rationale**: Allows incremental adoption without breaking existing code

## Testing Strategy

### Unit Tests
- Test each function in isolation
- Mock dependencies when needed
- Focus on single behaviors

### Integration Tests
- Test schema + registry together
- Test command execution flow
- Test event projection flow

### End-to-End Tests
- Full domain implementation
- API to storage flow
- Migration scenarios

## Example Test (t-wada style)

```typescript
import { describe, it, expect } from 'vitest';
import { z } from 'zod';
import { defineEvent } from '../event-schema.js';

describe('defineEvent', () => {
  it('creates event definition with type property', () => {
    // Arrange
    const definition = {
      type: 'UserCreated' as const,
      schema: z.object({ userId: z.string() })
    };

    // Act
    const result = defineEvent(definition);

    // Assert
    expect(result.type).toBe('UserCreated');
  });

  it('schema validates correct data', () => {
    // Arrange
    const UserCreated = defineEvent({
      type: 'UserCreated',
      schema: z.object({ userId: z.string() })
    });
    const validData = { userId: '123' };

    // Act
    const result = UserCreated.schema.safeParse(validData);

    // Assert
    expect(result.success).toBe(true);
  });

  it('create function adds type discriminator', () => {
    // Arrange
    const UserCreated = defineEvent({
      type: 'UserCreated',
      schema: z.object({ userId: z.string() })
    });
    const data = { userId: '123' };

    // Act
    const event = UserCreated.create(data);

    // Assert
    expect(event.type).toBe('UserCreated');
  });
});
```

## Next Steps

1. **Immediate**: Start writing event schema tests
2. **This Week**: Complete Phase 1 tests and implementation
3. **Next Week**: Begin code generation work
4. **Following Week**: Migration support and documentation

## Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| Breaking existing code | High | Compatibility layer, gradual migration |
| Performance regression | Medium | Benchmark before/after |
| Complex migration | Medium | Clear guide, codemods |
| Type inference issues | Low | Extensive testing |

## Success Criteria

1. All tests passing
2. No breaking changes for existing code
3. Full type safety maintained
4. Performance equal or better
5. Clear migration path documented
6. Working examples updated

## References

- [Original Design Document](./type-registry.md)
- [t-wada TDD Principles](https://github.com/twada/tdd-javascript-samples)
- [Zod Documentation](https://zod.dev)
- [ts-morph Documentation](https://ts-morph.com)