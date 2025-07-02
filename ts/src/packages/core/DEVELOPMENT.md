# @sekiban/core Development Guide

## Overview

This guide provides detailed information for developers working on the @sekiban/core package.

## Project Structure

```
core/
├── src/
│   ├── aggregates/      # Aggregate root implementations
│   ├── commands/        # Command handling logic
│   ├── documents/       # Document types (partition keys, IDs)
│   ├── events/          # Event definitions and storage
│   ├── executors/       # Command/query execution engines
│   ├── queries/         # Query handling and projections
│   ├── result/          # Error handling with Result type
│   ├── serialization/   # JSON serialization utilities
│   └── utils/           # Common utilities
├── package.json
├── tsconfig.json
├── tsup.config.ts
├── vitest.config.ts
└── README.md
```

## Development Setup

### Prerequisites

- Node.js 18+
- pnpm 8+
- TypeScript 5.3+

### Installation

```bash
# From the root directory
pnpm install

# Build the core package
pnpm -F @sekiban/core build

# Run tests
pnpm -F @sekiban/core test
```

## Testing Strategy

### Unit Tests

We follow TDD (Test-Driven Development) principles:

1. **Red**: Write a failing test
2. **Green**: Implement minimal code to pass
3. **Refactor**: Improve code quality

### Test Organization

- Place test files next to implementation: `module.ts` → `module.test.ts`
- Use descriptive test names that explain the behavior
- Follow AAA pattern: Arrange, Act, Assert

### Running Tests

```bash
# Run all tests
pnpm test

# Run tests in watch mode
pnpm test:watch

# Run with coverage
pnpm test:coverage

# Run specific test file
pnpm test partition-keys.test.ts
```

## Code Style Guidelines

### TypeScript Conventions

1. **Interfaces over Types** for object shapes
2. **Explicit return types** for public methods
3. **Immutability** - prefer `readonly` and avoid mutations
4. **Functional error handling** with Result type

### Example Code Style

```typescript
// Good: Explicit, immutable, functional
export interface EventDocument {
  readonly aggregateId: string
  readonly version: number
  readonly eventType: string
  readonly payload: unknown
  readonly timestamp: Date
}

export function createEventDocument(
  event: Event,
  partitionKeys: PartitionKeys,
  version: number
): Result<EventDocument, DomainError> {
  // Implementation
}

// Avoid: Mutations, exceptions for control flow
class EventStore {
  events: Event[] = [] // Avoid mutable arrays
  
  addEvent(event: Event) {
    this.events.push(event) // Avoid mutations
    if (!event.isValid()) {
      throw new Error('Invalid') // Avoid exceptions
    }
  }
}
```

## Key Concepts Implementation

### 1. Partition Keys

```typescript
// Creating partition keys for multi-tenancy
const keys = PartitionKeys.create(
  'user-123',        // aggregateId
  'users',           // group
  'tenant-abc'       // rootPartitionKey
)

// Generates: 'tenant-abc-users-user-123'
```

### 2. Event Streams

```typescript
// Event stream maintains ordered event history
const stream = new EventStream(partitionKeys)
stream.append(new UserCreated('user-123', 'John'))
stream.append(new UserUpdated('user-123', 'John Doe'))

// Version tracking
console.log(stream.getVersion()) // 2
```

### 3. Command Execution

```typescript
// Commands validate and produce events
class CreateUserCommand implements CommandWithHandler {
  getHandler() {
    return {
      validate: async (cmd) => {
        // Validation logic
        return ok(undefined)
      },
      handle: async (cmd, state) => {
        // Business logic
        return ok([new UserCreated(...)])
      }
    }
  }
}
```

### 4. Projectors

```typescript
// Projectors rebuild state from events
class UserProjector implements Projector<UserState> {
  getInitialState(): UserState {
    return { id: '', name: '', active: false }
  }
  
  apply(state: UserState, event: Event): UserState {
    if (event instanceof UserCreated) {
      return { ...state, id: event.userId, active: true }
    }
    return state
  }
}
```

## Building and Publishing

### Build Process

```bash
# Development build
pnpm build

# Production build
pnpm build --minify

# Watch mode
pnpm dev
```

### Pre-publish Checklist

- [ ] All tests passing
- [ ] Coverage > 90%
- [ ] No TypeScript errors
- [ ] Documentation updated
- [ ] Changeset created

### Creating a Changeset

```bash
# From root directory
pnpm changeset

# Select @sekiban/core
# Choose version bump type
# Write change description
```

## Debugging

### VSCode Launch Configuration

```json
{
  "type": "node",
  "request": "launch",
  "name": "Debug Core Tests",
  "autoAttachChildProcesses": true,
  "skipFiles": ["<node_internals>/**", "**/node_modules/**"],
  "program": "${workspaceRoot}/node_modules/vitest/vitest.mjs",
  "args": ["run", "${file}"],
  "smartStep": true,
  "console": "integratedTerminal"
}
```

### Common Issues

1. **Circular Dependencies**
   - Use `index.ts` files for proper module boundaries
   - Avoid importing from parent directories

2. **Type Inference Issues**
   - Explicitly type generic parameters
   - Use type assertions sparingly

3. **Test Flakiness**
   - Avoid time-dependent tests
   - Mock external dependencies
   - Use deterministic IDs in tests

## Performance Considerations

### Event Stream Optimization

- Snapshots created every 10 events by default
- Configurable snapshot interval
- Lazy loading of events

### Serialization

- Custom JSON serializer for dates and special types
- Avoid circular references
- Consider compression for large payloads

## Contributing

1. Create feature branch from `main`
2. Follow TDD approach
3. Ensure all tests pass
4. Update documentation
5. Create changeset
6. Submit PR with description

## Resources

- [Event Sourcing Pattern](https://martinfowler.com/eaaDev/EventSourcing.html)
- [CQRS Pattern](https://martinfowler.com/bliki/CQRS.html)
- [Domain-Driven Design](https://domainlanguage.com/ddd/)
- [Sekiban C# Documentation](https://github.com/J-Tech-Japan/Sekiban)
