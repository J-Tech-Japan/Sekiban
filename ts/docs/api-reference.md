# API Reference

## Schema Registry API

### Event Functions

#### `defineEvent<TName, TSchema>(definition)`

Creates an event definition with schema validation.

**Parameters:**
- `definition.type: TName` - Event type name (must be unique)
- `definition.schema: TSchema` - Zod schema for event data validation

**Returns:**
```typescript
{
  type: TName;
  schema: TSchema;
  create: (data: z.infer<TSchema>) => z.infer<TSchema> & { type: TName };
  parse: (data: unknown) => z.infer<TSchema> & { type: TName };
  safeParse: (data: unknown) => SafeParseReturnType<z.infer<TSchema>, z.infer<TSchema> & { type: TName }>;
}
```

**Example:**
```typescript
const UserCreated = defineEvent({
  type: 'UserCreated',
  schema: z.object({
    userId: z.string().uuid(),
    name: z.string(),
    email: z.string().email()
  })
});

// Create event
const event = UserCreated.create({
  userId: '123e4567-e89b-12d3-a456-426614174000',
  name: 'John Doe',
  email: 'john@example.com'
});

// Parse with validation
const parsed = UserCreated.parse(untrustedData); // throws on invalid data

// Safe parse
const result = UserCreated.safeParse(untrustedData);
if (result.success) {
  console.log(result.data);
}
```

### Command Functions

#### `defineCommand<TName, TSchema, TPayloadUnion>(definition)`

Creates a command definition with handlers.

**Parameters:**
- `definition.type: TName` - Command type name (must be unique)
- `definition.schema: TSchema` - Zod schema for command data validation
- `definition.aggregateType: string` - Target aggregate type for command routing
- `definition.handlers: CommandHandlers<z.infer<TSchema>, TPayloadUnion>` - Command handling logic

**Command Handlers:**
```typescript
interface CommandHandlers<TData, TPayloadUnion> {
  specifyPartitionKeys: (data: TData) => PartitionKeys;
  validate: (data: TData) => Result<void, CommandValidationError>;
  handle: (data: TData, aggregate: Aggregate<TPayloadUnion | EmptyAggregatePayload>) => Result<IEventPayload[], SekibanError>;
}
```

**Returns:**
```typescript
{
  type: TName;
  schema: TSchema;
  aggregateType: string;
  handlers: CommandHandlers<z.infer<TSchema>, TPayloadUnion>;
  create: (data: z.infer<TSchema>) => z.infer<TSchema> & { commandType: TName };
  validate: (data: unknown) => Result<void, CommandValidationError>;
  execute: (data: z.infer<TSchema>, aggregate: Aggregate<TPayloadUnion | EmptyAggregatePayload>) => Result<IEventPayload[], SekibanError>;
}
```

**Example:**
```typescript
const CreateUser = defineCommand({
  type: 'CreateUser',
  schema: z.object({
    name: z.string().min(1),
    email: z.string().email()
  }),
  aggregateType: 'User',
  handlers: {
    specifyPartitionKeys: () => PartitionKeys.generate('User'),
    validate: (data) => {
      if (data.email.endsWith('@test.com')) {
        return err({ type: 'ValidationError', message: 'Test emails not allowed' });
      }
      return ok(undefined);
    },
    handle: (data, aggregate) => {
      if (aggregate.payload.aggregateType !== 'Empty') {
        return err({ type: 'AggregateAlreadyExists', message: 'User already exists' });
      }
      return ok([UserCreated.create({ 
        userId: crypto.randomUUID(),
        name: data.name,
        email: data.email
      })]);
    }
  }
});
```

### Projector Functions

#### `defineProjector<TPayloadUnion>(definition)`

Creates a projector definition for aggregate state.

**Parameters:**
- `definition.aggregateType: string` - Aggregate type name
- `definition.initialState: () => EmptyAggregatePayload` - Factory for initial empty state
- `definition.projections: Record<string, ProjectionFunction>` - Event projection handlers

**Projection Function:**
```typescript
type ProjectionFunction<TState, TEvent> = (
  state: TState,
  event: TEvent
) => TState;
```

**Returns:**
```typescript
{
  aggregateType: string;
  getInitialState: (partitionKeys: PartitionKeys) => Aggregate<EmptyAggregatePayload>;
  project: (aggregate: Aggregate<TPayloadUnion | EmptyAggregatePayload>, event: IEvent) => Result<Aggregate<TPayloadUnion | EmptyAggregatePayload>, SekibanError>;
}
```

**Example:**
```typescript
const userProjector = defineProjector<UserPayload | EmptyAggregatePayload>({
  aggregateType: 'User',
  initialState: () => ({ aggregateType: 'Empty' as const }),
  projections: {
    UserCreated: (state, event) => ({
      aggregateType: 'User' as const,
      userId: event.userId,
      name: event.name,
      email: event.email
    }),
    UserUpdated: (state, event) => {
      if (state.aggregateType !== 'User') return state;
      return {
        ...state,
        name: event.name || state.name,
        email: event.email || state.email
      };
    }
  }
});
```

## Registry API

### SchemaRegistry

Central registry for schema-based domain types.

#### Methods

##### `registerEvent<T>(event: T): T`
Registers an event definition.

##### `registerCommand<T>(command: T): T`
Registers a command definition.

##### `registerProjector<T>(projector: T): T`
Registers a projector definition.

##### `getEventTypes(): string[]`
Returns all registered event type names.

##### `getCommandTypes(): string[]`
Returns all registered command type names.

##### `getProjectorTypes(): string[]`
Returns all registered projector aggregate types.

##### `deserializeEvent(eventType: string, data: unknown): any`
Deserializes and validates event data.

##### `clear(): void`
Clears all registrations (useful for testing).

### Global Registry Instance

```typescript
import { globalRegistry } from '@sekiban/core';

// Register types
globalRegistry.registerEvent(UserCreated);
globalRegistry.registerCommand(CreateUser);
globalRegistry.registerProjector(userProjector);
```

## Domain Types API

### `createSchemaDomainTypes(registry: SchemaRegistry): SekibanDomainTypes`

Creates a SekibanDomainTypes instance from a schema registry.

**Parameters:**
- `registry: SchemaRegistry` - Schema registry with registered types

**Returns:**
- `SekibanDomainTypes` - Interface for type operations used by executors

**Example:**
```typescript
const domainTypes = createSchemaDomainTypes(globalRegistry);
```

### SekibanDomainTypes Interface

```typescript
interface SekibanDomainTypes {
  readonly eventTypes: IEventTypes;
  readonly commandTypes: ICommandTypes;
  readonly projectorTypes: IProjectorTypes;
  readonly queryTypes: IQueryTypes;
  readonly aggregateTypes: IAggregateTypes;
  readonly serializer: ISekibanSerializer;
}
```

## Executor API

### `createInMemorySekibanExecutor(domainTypes: SekibanDomainTypes, config?: SekibanExecutorConfig): InMemorySekibanExecutorWithDomainTypes`

Creates an in-memory executor with domain types.

**Parameters:**
- `domainTypes: SekibanDomainTypes` - Domain types registry
- `config?: SekibanExecutorConfig` - Optional configuration

**Configuration Options:**
```typescript
interface SekibanExecutorConfig {
  enableSnapshots?: boolean;
  snapshotFrequency?: number;
  maxRetries?: number;
  retryDelayMs?: number;
}
```

**Example:**
```typescript
const executor = createInMemorySekibanExecutor(domainTypes, {
  enableSnapshots: true,
  snapshotFrequency: 100
});
```

### Executor Methods

#### `executeCommand(command: ICommand): Promise<Result<CommandResponse, SekibanError>>`
Executes a command and returns the result.

#### `loadAggregate<TPayload>(partitionKeys: PartitionKeys): Promise<Result<Aggregate<TPayload> | null, SekibanError>>`
Loads an aggregate by partition keys.

#### `queryEvents(filter: EventFilter): Promise<Result<Event[], SekibanError>>`
Queries events with optional filtering.

## Partition Keys API

### `PartitionKeys.generate(group: string, rootPartitionKey?: string): PartitionKeys`
Generates new partition keys for a new aggregate.

### `PartitionKeys.existing(group: string, aggregateId: string, rootPartitionKey?: string): PartitionKeys`
Creates partition keys for an existing aggregate.

**Example:**
```typescript
// New aggregate
const newKeys = PartitionKeys.generate('User');

// Existing aggregate
const existingKeys = PartitionKeys.existing('User', userId);

// Multi-tenant
const tenantKeys = PartitionKeys.generate('User', tenantId);
```

## Error Handling

All operations return `Result<T, E>` from neverthrow library.

**Example:**
```typescript
const result = await executor.executeCommand(command);

if (result.isOk()) {
  console.log('Success:', result.value);
} else {
  console.error('Error:', result.error);
}

// Or use match
result.match(
  (value) => console.log('Success:', value),
  (error) => console.error('Error:', error)
);
```

## Type Inference

The schema-based approach provides full TypeScript type inference:

```typescript
// Event type is inferred
const event = UserCreated.create({
  userId: '123', // must be string
  name: 'John',  // must be string
  email: 'john@example.com' // must be valid email
});

// TypeScript knows the shape
type UserCreatedEvent = ReturnType<typeof UserCreated.create>;
// { type: 'UserCreated'; userId: string; name: string; email: string; }
```