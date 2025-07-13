# Migration Guide: From Class-Based to Schema-Based Type Registry

This guide helps you migrate from the class-based type registration system to the new schema-based approach using Zod and SekibanDomainTypes.

## Overview

The new system provides:
- **Better type safety** with Zod schema validation
- **Runtime validation** at serialization boundaries
- **Centralized type registry** that all executors must use
- **No decorators or classes** required

## Key Changes

### 1. Event Definition

**Before (Class-based):**
```typescript
@RegisterEvent('UserCreated')
export class UserCreated implements IEventPayload {
  constructor(
    public readonly userId: string,
    public readonly name: string,
    public readonly email: string
  ) {}
}
```

**After (Schema-based):**
```typescript
import { z } from 'zod';
import { defineEvent } from '@sekiban/core';

export const UserCreated = defineEvent({
  type: 'UserCreated',
  schema: z.object({
    userId: z.string().uuid(),
    name: z.string().min(1),
    email: z.string().email()
  })
});
```

### 2. Command Definition

**Before (Class-based):**
```typescript
@RegisterCommand('CreateUser')
export class CreateUser implements ICommand<UserAggregate> {
  constructor(
    public readonly name: string,
    public readonly email: string
  ) {}

  validate(): Result<void, ValidationError> {
    if (this.email.endsWith('@test.com')) {
      return err(new ValidationError('Test emails not allowed'));
    }
    return ok(undefined);
  }

  async execute(
    aggregate: UserAggregate
  ): Promise<Result<IEventPayload[], SekibanError>> {
    if (!aggregate.isEmpty()) {
      return err(new ValidationError('User already exists'));
    }
    return ok([new UserCreated(uuid(), this.name, this.email)]);
  }
}
```

**After (Schema-based):**
```typescript
import { z } from 'zod';
import { defineCommand } from '@sekiban/core';

export const CreateUser = defineCommand({
  type: 'CreateUser',
  schema: z.object({
    name: z.string().min(1),
    email: z.string().email()
  }),
  aggregateType: 'User', // Explicit aggregate type
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
        return err({ type: 'ValidationError', message: 'User already exists' });
      }
      return ok([
        UserCreated.create({
          userId: crypto.randomUUID(),
          name: data.name,
          email: data.email
        })
      ]);
    }
  }
});
```

### 3. Projector Definition

**Before (Class-based):**
```typescript
@RegisterProjector('UserProjector')
export class UserProjector extends AggregateProjector<UserAggregate> {
  project(aggregate: UserAggregate, event: IEvent): UserAggregate {
    switch (event.eventType) {
      case 'UserCreated':
        const created = event.payload as UserCreated;
        return new UserAggregate(
          created.userId,
          created.name,
          created.email
        );
      case 'UserUpdated':
        const updated = event.payload as UserUpdated;
        return new UserAggregate(
          aggregate.userId,
          updated.name || aggregate.name,
          updated.email || aggregate.email
        );
      default:
        return aggregate;
    }
  }
}
```

**After (Schema-based):**
```typescript
import { defineProjector } from '@sekiban/core';

export const userProjector = defineProjector<UserPayload | EmptyAggregatePayload>({
  aggregateType: 'User',
  initialState: () => ({ aggregateType: 'Empty' as const }),
  projections: {
    UserCreated: (state, event: ReturnType<typeof UserCreated.create>) => ({
      aggregateType: 'User' as const,
      userId: event.userId,
      name: event.name,
      email: event.email
    }),
    UserUpdated: (state, event: ReturnType<typeof UserUpdated.create>) => {
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

### 4. Type Registration

**Before (Class-based):**
```typescript
// Automatic registration via decorators
// No explicit registration needed
```

**After (Schema-based):**
```typescript
import { globalRegistry, createSchemaDomainTypes } from '@sekiban/core';

// Register all schemas
globalRegistry.registerEvent(UserCreated);
globalRegistry.registerEvent(UserUpdated);
globalRegistry.registerCommand(CreateUser);
globalRegistry.registerCommand(UpdateUser);
globalRegistry.registerProjector(userProjector);

// Create SekibanDomainTypes
const domainTypes = createSchemaDomainTypes(globalRegistry);
```

### 5. Executor Usage

**Before (Class-based):**
```typescript
const executor = new InMemorySekibanExecutor();
executor.registerProjector(new UserProjector());
executor.registerCommandMapping('CreateUser', 'User');

const command = new CreateUser('John Doe', 'john@example.com');
const result = await executor.executeCommand(command);
```

**After (Schema-based with SekibanDomainTypes):**
```typescript
import { createInMemorySekibanExecutor } from '@sekiban/core';

// Create executor with domain types
const executor = createInMemorySekibanExecutor(domainTypes);

// Create and execute command
const command = CreateUser.create({
  name: 'John Doe',
  email: 'john@example.com'
});
const result = await executor.executeCommand(command);
```

## Migration Steps

### Step 1: Install Dependencies
```bash
npm install zod
```

### Step 2: Convert Events
1. Replace event classes with `defineEvent` calls
2. Define Zod schemas for event data
3. Export the event definition

### Step 3: Convert Commands
1. Replace command classes with `defineCommand` calls
2. Add explicit `aggregateType` field
3. Move validation logic to `handlers.validate`
4. Move execution logic to `handlers.handle`

### Step 4: Convert Projectors
1. Replace projector classes with `defineProjector` calls
2. Define projection functions for each event type
3. Specify initial state function

### Step 5: Create Registry
1. Register all events, commands, and projectors with `globalRegistry`
2. Create `SekibanDomainTypes` using `createSchemaDomainTypes()`
3. Use this instance for all executors

### Step 6: Update Executors
1. Replace executor constructors to accept `SekibanDomainTypes`
2. Remove manual projector registration
3. Remove command-to-aggregate mapping

## Benefits of Migration

1. **Type Safety**: Full TypeScript inference from schemas
2. **Runtime Validation**: Automatic validation using Zod
3. **Consistency**: All executors use the same type registry
4. **Simpler Code**: No classes or decorators needed
5. **Better Performance**: No class instantiation overhead

## Common Patterns

### Creating Events with Validation
```typescript
const result = UserCreated.safeParse(untrustedData);
if (result.success) {
  // result.data is fully typed
  console.log(result.data.userId);
} else {
  // Handle validation errors
  console.error(result.error);
}
```

### Dynamic Command Execution
```typescript
const commandType = req.params.type;
const commandDef = domainTypes.commandTypes.getCommandTypeByName(commandType);
if (commandDef) {
  const command = { commandType, ...req.body };
  const result = await executor.executeCommand(command);
}
```

### Using with Dapr
```typescript
import { SekibanDaprExecutor } from '@sekiban/dapr';

const daprExecutor = new SekibanDaprExecutor(
  daprClient,
  domainTypes, // Pass domain types
  {
    stateStoreName: 'statestore',
    pubSubName: 'pubsub',
    eventTopicName: 'events',
    actorType: 'AggregateActor'
  }
);
```

## Troubleshooting

### Issue: "Unknown event type"
Make sure all events are registered with the global registry before creating domain types.

### Issue: "Cannot determine aggregate type for command"
Ensure all commands have the `aggregateType` field specified in their definition.

### Issue: "No projector registered for aggregate type"
Verify that the projector's `aggregateType` matches the command's `aggregateType`.

## Backward Compatibility

During migration, you can run both systems in parallel:
1. Keep existing class-based code working
2. Gradually convert to schema-based definitions
3. Use adapters if needed for interoperability

Once migration is complete, remove:
- All decorator imports
- Class definitions for events/commands
- Manual type registration code

## Next Steps

- See `examples/schema-based-example.ts` for a complete working example
- Check the API documentation for detailed method signatures
- Join our community for migration support