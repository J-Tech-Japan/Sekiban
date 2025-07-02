# @sekiban/core

Core event sourcing and CQRS framework for TypeScript.

## Installation

```bash
npm install @sekiban/core
```

## Features

- **Event Sourcing**: Store domain events as the source of truth
- **CQRS**: Separate command and query responsibilities
- **Type-Safe**: Full TypeScript support with strong typing
- **Flexible**: Pluggable storage backends and serialization
- **Testing**: Built-in in-memory implementations for testing

## Quick Start

```typescript
import { 
  IEventPayload, 
  IAggregatePayload,
  AggregateProjector,
  CommandHandler,
  InMemorySekibanExecutor,
  ok
} from '@sekiban/core';

// Define events
class UserCreated implements IEventPayload {
  readonly eventType = 'UserCreated';
  constructor(public readonly name: string, public readonly email: string) {}
}

// Define aggregate state
class User implements IAggregatePayload {
  readonly aggregateType = 'User';
  constructor(
    public readonly name: string,
    public readonly email: string,
    public readonly createdAt: Date
  ) {}
}

// Define projector
class UserProjector extends AggregateProjector<User> {
  constructor() {
    super('User');
    
    this.on(UserCreated, (aggregate, event) => ({
      ...aggregate,
      payload: new User(event.name, event.email, new Date())
    }));
  }

  getInitialState(partitionKeys) {
    return {
      partitionKeys,
      aggregateType: 'User',
      version: 0,
      payload: new User('', '', new Date())
    };
  }
}

// Define command
class CreateUser {
  readonly commandType = 'CreateUser';
  constructor(public readonly name: string, public readonly email: string) {}
}

// Define command handler
const createUserHandler = new CommandHandler('CreateUser', 'User', {
  handle: (command: CreateUser) => ok([new UserCreated(command.name, command.email)])
});

// Create executor
const executor = new InMemorySekibanExecutor();
executor.registerProjector(new UserProjector());
// Register handler...

// Execute command
const result = await executor.executeCommand(
  new CreateUser('John Doe', 'john@example.com'),
  PartitionKeys.generate()
);
```

## Core Concepts

### Events
Events represent facts that have happened in your domain. They are immutable and stored in sequence.

### Aggregates
Aggregates are domain objects that encapsulate business logic and state. State is derived from events.

### Commands
Commands represent intentions to change the system state. They are validated and produce events.

### Queries
Queries retrieve data without modifying state. They can read from aggregates or projections.

### Projections
Projections build read models from events, optimized for specific query patterns.

## API Reference

See the [API documentation](https://github.com/J-Tech-Japan/Sekiban-ts) for detailed reference.

## License

MIT