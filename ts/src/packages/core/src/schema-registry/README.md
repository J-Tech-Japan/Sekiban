# Sekiban Schema Registry

A schema-first approach to Event Sourcing and CQRS using Zod validation.

## Overview

The Schema Registry provides a modern, type-safe way to define domain events, commands, and projectors using Zod schemas instead of traditional classes. This approach offers better serialization, validation, and type inference.

## Features

- 🎯 **Schema-First Design**: Define domain types as data schemas
- ✅ **Runtime Validation**: Automatic validation using Zod
- 🔍 **Full Type Safety**: Complete TypeScript type inference
- 📦 **Zero Boilerplate**: No classes or decorators needed
- 🚀 **High Performance**: Optimized for event sourcing workloads
- 🔄 **Multi-Projection Queries**: Cross-aggregate data queries

## Installation

```bash
npm install @sekiban/core zod neverthrow
```

## Quick Start

### 1. Define Events

```typescript
import { defineEvent } from '@sekiban/core/schema-registry';
import { z } from 'zod';

export const UserCreated = defineEvent({
  type: 'UserCreated',
  schema: z.object({
    userId: z.string(),
    email: z.string().email(),
    name: z.string()
  })
});

// Create event instance
const event = UserCreated.create({
  userId: '123',
  email: 'user@example.com',
  name: 'John Doe'
});
```

### 2. Define Commands

```typescript
import { defineCommand } from '@sekiban/core/schema-registry';
import { PartitionKeys } from '@sekiban/core';
import { ok, err } from 'neverthrow';

export const CreateUser = defineCommand({
  type: 'CreateUser',
  schema: z.object({
    email: z.string().email(),
    name: z.string().min(1)
  }),
  handlers: {
    // Specify aggregate partition keys
    specifyPartitionKeys: () => PartitionKeys.generate('User'),
    
    // Business validation beyond schema
    validate: (data) => {
      if (data.email.endsWith('@example.com')) {
        return err(new ValidationError('Example emails not allowed'));
      }
      return ok(undefined);
    },
    
    // Generate events
    handle: (data, aggregate) => {
      const userId = aggregate.partitionKeys.aggregateId;
      return ok([
        UserCreated.create({
          userId,
          email: data.email,
          name: data.name
        })
      ]);
    }
  }
});
```

### 3. Define Projectors

```typescript
import { defineProjector } from '@sekiban/core/schema-registry';

export const UserProjector = defineProjector({
  aggregateType: 'User',
  initialState: () => ({ aggregateType: 'Empty' as const }),
  projections: {
    UserCreated: (state, event) => ({
      aggregateType: 'User' as const,
      userId: event.userId,
      email: event.email,
      name: event.name,
      createdAt: new Date().toISOString()
    }),
    
    UserUpdated: (state, event) => ({
      ...state,
      aggregateType: 'User' as const,
      name: event.name ?? state.name,
      email: event.email ?? state.email,
      updatedAt: new Date().toISOString()
    })
  }
});
```

### 4. Setup Registry and Executor

```typescript
import { SchemaRegistry, SchemaExecutor } from '@sekiban/core/schema-registry';
import { InMemoryEventStore } from '@sekiban/core';

// Create registry
const registry = new SchemaRegistry();

// Register domain types
registry.registerEvent(UserCreated);
registry.registerCommand(CreateUser);
registry.registerProjector(UserProjector);

// Create executor
const eventStore = new InMemoryEventStore();
const executor = new SchemaExecutor({ registry, eventStore });

// Execute command
const result = await executor.executeCommand(CreateUser, {
  email: 'john@example.com',
  name: 'John Doe'
});

if (result.isOk()) {
  console.log('User created:', result.value.aggregateId);
}
```

## API Reference

### Event Definition

```typescript
defineEvent<T>({
  type: string;           // Unique event type name
  schema: z.ZodType<T>;   // Zod schema for validation
}): EventDefinition<T>
```

### Command Definition

```typescript
defineCommand<TData, TPayload>({
  type: string;
  schema: z.ZodType<TData>;
  handlers: {
    specifyPartitionKeys: (data: TData) => PartitionKeys;
    validate: (data: TData) => Result<void, ValidationError>;
    handle: (data: TData, aggregate: Aggregate) => Result<IEventPayload[], Error>;
  };
}): CommandDefinition<TData, TPayload>
```

### Projector Definition

```typescript
defineProjector<TPayloadUnion>({
  aggregateType: string;
  initialState: () => EmptyAggregatePayload;
  projections: {
    [eventType: string]: (state: TPayloadUnion, event: any) => TPayloadUnion;
  };
}): ProjectorDefinition<TPayloadUnion>
```

### Schema Registry

```typescript
class SchemaRegistry {
  // Register domain types
  registerEvent(event: EventDefinition): void;
  registerCommand(command: CommandDefinition): void;
  registerProjector(projector: ProjectorDefinition): void;
  
  // Query registry
  getEventTypes(): string[];
  getCommandTypes(): string[];
  getProjectorTypes(): string[];
  
  // Deserialize events
  deserializeEvent(type: string, data: unknown): any;
  safeDeserializeEvent(type: string, data: unknown): SafeParseResult<any>;
}
```

### Schema Executor

```typescript
class SchemaExecutor {
  // Execute commands
  executeCommand<T>(
    command: CommandDefinition<T>, 
    data: T
  ): Promise<Result<CommandResponse, Error>>;
  
  // Query aggregates
  queryAggregate<T>(
    partitionKeys: PartitionKeys,
    projector?: ProjectorDefinition<T>
  ): Promise<Result<QueryResponse<Aggregate<T>>, Error>>;
  
  // Multi-projection queries
  executeMultiProjectionQuery<T>(
    query: IMultiProjectionQuery<any, any, T>
  ): Promise<Result<QueryResponse<T>, Error>>;
}
```

## Advanced Usage

### Multi-Projection Queries

Query across multiple aggregates:

```typescript
export class OrdersByCustomerQuery implements IMultiProjectionQuery<any, any, Order[]> {
  constructor(private customerId: string) {}

  query = async (events: IEvent[]): Promise<Order[]> => {
    const orders = new Map<string, Order>();
    
    for (const event of events) {
      if (event.aggregateType !== 'Order') continue;
      
      // Build order state from events
      // ...
    }
    
    return Array.from(orders.values())
      .filter(order => order.customerId === this.customerId);
  };
}

// Execute query
const query = new OrdersByCustomerQuery('customer-123');
const result = await executor.executeMultiProjectionQuery(query);
```

### Schema Composition

Build complex schemas from simpler ones:

```typescript
// Shared schemas
const MoneySchema = z.object({
  amount: z.number().positive(),
  currency: z.string().length(3)
});

const AddressSchema = z.object({
  street: z.string(),
  city: z.string(),
  zipCode: z.string(),
  country: z.string()
});

// Compose into event
export const OrderPlaced = defineEvent({
  type: 'OrderPlaced',
  schema: z.object({
    orderId: z.string(),
    totalAmount: MoneySchema,
    shippingAddress: AddressSchema,
    items: z.array(z.object({
      productId: z.string(),
      price: MoneySchema,
      quantity: z.number().int().positive()
    }))
  })
});
```

## Examples

See the [examples](./examples) directory for complete examples:

- **E-commerce Domain**: Full implementation of orders, inventory, and multi-projection queries
- **Banking Domain**: Account management with transactions and balance tracking
- **Task Management**: State machine implementation with task lifecycle

## Testing

The schema-first approach makes testing straightforward:

```typescript
describe('User Domain', () => {
  it('creates user with valid data', async () => {
    const result = await executor.executeCommand(CreateUser, {
      email: 'test@example.com',
      name: 'Test User'
    });
    
    expect(result.isOk()).toBe(true);
  });
  
  it('rejects invalid email', () => {
    expect(() => {
      CreateUser.create({ email: 'invalid', name: 'Test' });
    }).toThrow();
  });
});
```

## Best Practices

1. **Keep schemas simple** - Avoid deeply nested structures
2. **Version events** - Never modify existing event schemas
3. **Validate at boundaries** - Use schema validation at system entry points
4. **Use composition** - Build complex schemas from simple, reusable parts
5. **Test thoroughly** - Test both happy paths and validation failures

## Performance

- Schemas are parsed once and cached
- Validation is optimized by Zod
- No runtime reflection or metadata
- Minimal memory overhead

## Contributing

Contributions are welcome! Please read our contributing guidelines and submit pull requests to our repository.

## License

MIT