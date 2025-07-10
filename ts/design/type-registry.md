# TypeScript Type Registry Design for Sekiban (Zod-Based Schema-First Approach)

## Overview

The Sekiban event sourcing framework requires a robust type registration and discovery system to handle domain types (events, commands, aggregates, projectors, queries) at runtime. In C#, this is achieved through source generators that create a `SekibanDomainTypes` registry at compile time. This document outlines a modern TypeScript implementation using Zod schemas and a class-free architecture.

## Background: C# SekibanDomainTypes

In C# Sekiban, the `SekibanDomainTypes` system provides:

1. **Automatic Type Discovery** - Source generators scan the codebase for types implementing domain interfaces
2. **Type Registration** - All domain types are registered in a central registry
3. **Runtime Type Resolution** - Types can be looked up by name for deserialization
4. **Type-Safe Operations** - Strong typing throughout the system
5. **Serialization Context** - Proper JSON serialization configuration for all types

## Design Goals

1. **Type Safety** - Full TypeScript type inference from Zod schemas
2. **Runtime Validation** - Built-in validation through Zod
3. **Class-Free Architecture** - Pure functions and data structures only
4. **Developer Experience** - Simple, intuitive API with minimal boilerplate
5. **Performance** - Tree-shakeable code with minimal runtime overhead
6. **Compatibility** - Gradual migration path from existing code

## Technical Approach

### Schema-First Design with Zod

The new approach eliminates classes entirely in favor of:

1. **Zod schemas** for type definition and runtime validation
2. **Pure functions** for business logic (commands, projections)
3. **Build-time code generation** for type discovery
4. **Type-safe factories** for creating domain objects

## Implementation Design

### 1. Event Schema System

```typescript
// packages/core/src/schema-registry/event-schema.ts
import { z } from 'zod';

export interface EventSchemaDefinition<TName extends string, TSchema extends z.ZodTypeAny> {
  type: TName;
  schema: TSchema;
}

export function defineEvent<TName extends string, TSchema extends z.ZodTypeAny>(
  definition: EventSchemaDefinition<TName, TSchema>
) {
  return {
    type: definition.type,
    schema: definition.schema,
    create: (data: z.infer<TSchema>): z.infer<TSchema> & { type: TName } => ({
      type: definition.type,
      ...data
    }),
    parse: (data: unknown) => {
      const parsed = definition.schema.parse(data);
      return { type: definition.type, ...parsed };
    },
    safeParse: (data: unknown) => {
      const result = definition.schema.safeParse(data);
      if (result.success) {
        return { success: true, data: { type: definition.type, ...result.data } };
      }
      return result;
    }
  } as const;
}

// Usage example
export const UserCreated = defineEvent({
  type: 'UserCreated',
  schema: z.object({
    userId: z.string().uuid(),
    name: z.string().min(1),
    email: z.string().email(),
    createdAt: z.string().datetime()
  })
});

// Type inference works automatically
type UserCreatedEvent = ReturnType<typeof UserCreated.create>;
// { type: 'UserCreated'; userId: string; name: string; email: string; createdAt: string }
```

### 2. Command Schema System

```typescript
// packages/core/src/schema-registry/command-schema.ts
import { z } from 'zod';
import type { Result } from 'neverthrow';
import type { PartitionKeys } from '../documents/partition-keys.js';
import type { IEventPayload } from '../events/event-payload.js';
import type { Aggregate } from '../aggregates/aggregate.js';

export interface CommandHandlers<TData, TPayloadUnion> {
  specifyPartitionKeys: (data: TData) => PartitionKeys;
  validate: (data: TData) => Result<void, CommandValidationError>;
  handle: (
    data: TData,
    aggregate: Aggregate<TPayloadUnion | EmptyAggregatePayload>
  ) => Result<IEventPayload[], SekibanError>;
}

export interface CommandSchemaDefinition<
  TName extends string,
  TSchema extends z.ZodTypeAny,
  TPayloadUnion
> {
  type: TName;
  schema: TSchema;
  handlers: CommandHandlers<z.infer<TSchema>, TPayloadUnion>;
}

export function defineCommand<
  TName extends string,
  TSchema extends z.ZodTypeAny,
  TPayloadUnion
>(definition: CommandSchemaDefinition<TName, TSchema, TPayloadUnion>) {
  return {
    type: definition.type,
    schema: definition.schema,
    handlers: definition.handlers,
    create: (data: z.infer<TSchema>) => ({
      commandType: definition.type,
      ...data
    }),
    validate: (data: unknown) => {
      const parseResult = definition.schema.safeParse(data);
      if (!parseResult.success) {
        return err(new CommandValidationError(
          definition.type,
          parseResult.error.errors.map(e => e.message)
        ));
      }
      return definition.handlers.validate(parseResult.data);
    },
    execute: (data: z.infer<TSchema>, aggregate: Aggregate<TPayloadUnion | EmptyAggregatePayload>) => {
      return definition.handlers.handle(data, aggregate);
    }
  } as const;
}

// Usage example
export const CreateUser = defineCommand({
  type: 'CreateUser',
  schema: z.object({
    name: z.string().min(1),
    email: z.string().email()
  }),
  handlers: {
    specifyPartitionKeys: () => PartitionKeys.generate('User'),
    validate: (data) => {
      // Additional business validation beyond schema
      if (data.email.endsWith('@test.com')) {
        return err(new CommandValidationError('CreateUser', ['Test emails not allowed']));
      }
      return ok(undefined);
    },
    handle: (data, aggregate) => {
      if (aggregate.payload.aggregateType !== 'Empty') {
        return err(new ValidationError('User already exists'));
      }
      return ok([UserCreated.create({
        userId: generateId(),
        name: data.name,
        email: data.email,
        createdAt: new Date().toISOString()
      })]);
    }
  }
});
```

### 3. Projector Configuration System

```typescript
// packages/core/src/schema-registry/projector-schema.ts
export interface ProjectorDefinition<TPayloadUnion extends ITypedAggregatePayload> {
  aggregateType: string;
  initialState: () => EmptyAggregatePayload;
  projections: {
    [eventType: string]: (
      state: TPayloadUnion | EmptyAggregatePayload,
      event: any
    ) => TPayloadUnion | EmptyAggregatePayload;
  };
}

export function defineProjector<TPayloadUnion extends ITypedAggregatePayload>(
  definition: ProjectorDefinition<TPayloadUnion>
) {
  return {
    aggregateType: definition.aggregateType,
    getInitialState: (partitionKeys: PartitionKeys) => new Aggregate(
      partitionKeys,
      definition.aggregateType,
      0,
      definition.initialState(),
      null,
      definition.aggregateType,
      1
    ),
    project: (
      aggregate: Aggregate<TPayloadUnion | EmptyAggregatePayload>,
      event: IEvent
    ): Result<Aggregate<TPayloadUnion | EmptyAggregatePayload>, SekibanError> => {
      const projection = definition.projections[event.eventType];
      if (!projection) {
        return ok(aggregate); // Event not handled by this projector
      }
      
      try {
        const newPayload = projection(aggregate.payload, event.payload);
        return ok(new Aggregate(
          aggregate.partitionKeys,
          aggregate.aggregateType,
          aggregate.version + 1,
          newPayload,
          SortableUniqueId.generate(),
          aggregate.projectorTypeName,
          aggregate.projectorVersion
        ));
      } catch (error) {
        return err(new ValidationError(
          `Projection failed: ${error instanceof Error ? error.message : 'Unknown error'}`
        ));
      }
    }
  };
}

// Usage example
export const userProjector = defineProjector<UserPayload | DeletedUserPayload>({
  aggregateType: 'User',
  initialState: () => ({ aggregateType: 'Empty' as const }),
  projections: {
    UserCreated: (state, event: ReturnType<typeof UserCreated.create>) => ({
      aggregateType: 'User' as const,
      userId: event.userId,
      name: event.name,
      email: event.email,
      createdAt: event.createdAt
    }),
    UserUpdated: (state, event) => {
      if (state.aggregateType !== 'User') return state;
      return { ...state, ...event };
    },
    UserDeleted: (state) => ({
      aggregateType: 'DeletedUser' as const
    })
  }
});
```

### 4. Schema Registry Implementation

```typescript
// packages/core/src/schema-registry/registry.ts
import { z } from 'zod';

export class SchemaRegistry {
  private eventSchemas = new Map<string, z.ZodTypeAny>();
  private commandDefinitions = new Map<string, any>();
  private projectorDefinitions = new Map<string, any>();

  registerEvent<T extends { type: string; schema: z.ZodTypeAny }>(event: T) {
    this.eventSchemas.set(event.type, event.schema);
    return event;
  }

  registerCommand<T extends { type: string }>(command: T) {
    this.commandDefinitions.set(command.type, command);
    return command;
  }

  registerProjector<T extends { aggregateType: string }>(projector: T) {
    this.projectorDefinitions.set(projector.aggregateType, projector);
    return projector;
  }

  deserializeEvent(type: string, data: unknown) {
    const schema = this.eventSchemas.get(type);
    if (!schema) {
      throw new Error(`Unknown event type: ${type}`);
    }
    return { type, ...schema.parse(data) };
  }

  getCommand(type: string) {
    return this.commandDefinitions.get(type);
  }

  getProjector(aggregateType: string) {
    return this.projectorDefinitions.get(aggregateType);
  }
}

// Global registry instance
export const globalRegistry = new SchemaRegistry();
```

### 5. File Organization Pattern

```typescript
// domain/user/events/user-created.event.ts
import { z } from 'zod';
import { defineEvent } from '@sekiban/core';
import { globalRegistry } from '@sekiban/core';

export const UserCreated = globalRegistry.registerEvent(
  defineEvent({
    type: 'UserCreated',
    schema: z.object({
      userId: z.string().uuid(),
      name: z.string().min(1),
      email: z.string().email(),
      createdAt: z.string().datetime()
    })
  })
);

// domain/user/commands/create-user.command.ts
import { defineCommand } from '@sekiban/core';
import { globalRegistry } from '@sekiban/core';
import { UserCreated } from '../events/user-created.event.js';

export const CreateUser = globalRegistry.registerCommand(
  defineCommand({
    type: 'CreateUser',
    schema: z.object({
      name: z.string().min(1),
      email: z.string().email()
    }),
    handlers: {
      // ... handlers implementation
    }
  })
);

// domain/user/projectors/user.projector.ts
import { defineProjector } from '@sekiban/core';
import { globalRegistry } from '@sekiban/core';

export const userProjector = globalRegistry.registerProjector(
  defineProjector({
    aggregateType: 'User',
    // ... projector configuration
  })
);
```

### 6. Build-Time Code Generation

```typescript
// packages/codegen/src/schema-scanner.ts
import { Project } from 'ts-morph';
import * as path from 'path';
import * as fs from 'fs';

export class SchemaScanner {
  constructor(private project: Project) {}

  async scanSchemas() {
    const schemas = {
      events: [] as Array<{ file: string; name: string; type: string }>,
      commands: [] as Array<{ file: string; name: string; type: string }>,
      projectors: [] as Array<{ file: string; name: string; aggregateType: string }>
    };

    // Scan for defineEvent calls
    this.project.getSourceFiles().forEach(sourceFile => {
      sourceFile.getDescendantsOfKind(ts.SyntaxKind.CallExpression).forEach(call => {
        const expression = call.getExpression();
        if (expression.getText() === 'defineEvent') {
          // Extract event information
          const firstArg = call.getArguments()[0];
          // ... parse schema definition
        }
      });
    });

    return schemas;
  }

  generateRegistry(schemas: any) {
    return `
// Auto-generated - do not edit
import { z } from 'zod';

${schemas.events.map(e => 
  `import { ${e.name} } from '${e.file}';`
).join('\n')}

${schemas.commands.map(c => 
  `import { ${c.name} } from '${c.file}';`
).join('\n')}

${schemas.projectors.map(p => 
  `import { ${p.name} } from '${p.file}';`
).join('\n')}

export const eventRegistry = {
${schemas.events.map(e => 
  `  '${e.type}': ${e.name},`
).join('\n')}
} as const;

export const commandRegistry = {
${schemas.commands.map(c => 
  `  '${c.type}': ${c.name},`
).join('\n')}
} as const;

export const projectorRegistry = {
${schemas.projectors.map(p => 
  `  '${p.aggregateType}': ${p.name},`
).join('\n')}
} as const;

// Type exports
export type EventType = keyof typeof eventRegistry;
export type CommandType = keyof typeof commandRegistry;
export type AggregateType = keyof typeof projectorRegistry;

// Union types for exhaustive checking
export type AnyEvent = ReturnType<typeof eventRegistry[EventType]['create']>;
export type AnyCommand = ReturnType<typeof commandRegistry[CommandType]['create']>;
`;
  }
}
```

## Usage Examples

### 1. Defining Domain Types

```typescript
// domain/user/events/user-events.ts
import { z } from 'zod';
import { defineEvent } from '@sekiban/core';

export const UserCreated = defineEvent({
  type: 'UserCreated',
  schema: z.object({
    userId: z.string().uuid(),
    name: z.string().min(1),
    email: z.string().email(),
    createdAt: z.string().datetime()
  })
});

export const UserDeleted = defineEvent({
  type: 'UserDeleted',
  schema: z.object({
    userId: z.string().uuid(),
    deletedAt: z.string().datetime(),
    reason: z.string()
  })
});
```

### 2. Creating Events with Type Safety

```typescript
// Full type inference and validation
const event = UserCreated.create({
  userId: '123e4567-e89b-12d3-a456-426614174000',
  name: 'John Doe',
  email: 'john@example.com',
  createdAt: new Date().toISOString()
});

// Runtime validation
const parsed = UserCreated.safeParse(untrustedData);
if (parsed.success) {
  // parsed.data is fully typed
  console.log(parsed.data.userId);
}
```

### 3. Command Execution

```typescript
import { SekibanExecutor } from '@sekiban/core';
import { CreateUser } from './commands/create-user.js';

const executor = new SekibanExecutor(eventStore, registry);

// Type-safe command creation
const command = CreateUser.create({
  name: 'John Doe',
  email: 'john@example.com'
});

// Validation happens automatically
const result = await executor.execute(command);
```

### 4. Dynamic Type Resolution

```typescript
// Deserialize event from storage
const eventData = await eventStore.getEvent(eventId);
const EventDefinition = eventRegistry[eventData.type];
if (EventDefinition) {
  const event = EventDefinition.parse(eventData.payload);
  // event is fully typed based on the event type
}

// API command handling
app.post('/api/command/:type', async (req, res) => {
  const CommandDefinition = commandRegistry[req.params.type];
  if (!CommandDefinition) {
    return res.status(404).json({ error: 'Unknown command type' });
  }
  
  const validation = CommandDefinition.schema.safeParse(req.body);
  if (!validation.success) {
    return res.status(400).json({ errors: validation.error.errors });
  }
  
  const result = await executor.execute(
    CommandDefinition.create(validation.data)
  );
  res.json(result);
});
```

## Migration Strategy

### Phase 1: Parallel Implementation
1. Keep existing class-based code working
2. Add schema definitions alongside classes
3. Create adapters for interoperability

```typescript
// Adapter for existing class-based events
export function adaptClassToSchema<T extends IEventPayload>(
  EventClass: new (...args: any[]) => T,
  schema: z.ZodSchema<T>
) {
  return defineEvent({
    type: EventClass.name,
    schema,
    create: (data: z.infer<typeof schema>) => new EventClass(...Object.values(data))
  });
}
```

### Phase 2: Gradual Migration
1. Migrate events first (simplest)
2. Then commands (requires handler refactoring)
3. Finally projectors (most complex)

### Phase 3: Cleanup
1. Remove class definitions
2. Remove decorators
3. Update all imports

## Build Process Integration

### Development Mode

```json
{
  "scripts": {
    "dev": "concurrently \"npm:watch:*\"",
    "watch:schemas": "nodemon --watch 'src/**/*.ts' --exec 'npm run generate:registry'",
    "watch:build": "tsc --watch",
    "generate:registry": "sekiban-codegen generate"
  }
}
```

### Production Build

```json
{
  "scripts": {
    "build": "npm run generate:registry && tsc",
    "generate:registry": "sekiban-codegen generate --output src/generated/registry.ts"
  }
}
```

## Benefits Over Class-Based Approach

1. **No Classes** - Pure data and functions, better for functional programming
2. **Built-in Validation** - Zod provides runtime validation out of the box
3. **Better Tree-Shaking** - Only import what you use
4. **Simpler Testing** - Pure functions are easier to test
5. **Full Type Inference** - No need for manual type annotations
6. **Smaller Bundle Size** - No class prototype chains or decorators
7. **Better Performance** - No instantiation overhead

## Comparison with Previous Design

| Aspect | Class-Based (Old) | Schema-Based (New) |
|--------|-------------------|-------------------|
| Type Definition | Classes with decorators | Zod schemas |
| Validation | Manual in validate() method | Built-in with Zod |
| Registration | Decorator side-effects | Explicit registration |
| Bundle Size | Larger (includes decorators) | Smaller (tree-shakeable) |
| Type Safety | Good | Excellent (full inference) |
| Runtime Cost | Class instantiation | Object creation only |
| Testing | Mock classes | Pure functions |

## Implementation Roadmap

### Phase 1: Core Schema System (Week 1)
- [x] Design schema definition functions
- [ ] Implement event schema system
- [ ] Implement command schema system
- [ ] Implement projector configuration
- [ ] Create schema registry

### Phase 2: Code Generation (Week 2)
- [ ] Build schema scanner
- [ ] Create code generator
- [ ] Add CLI tool
- [ ] Implement watch mode

### Phase 3: Migration Support (Week 3)
- [ ] Create compatibility adapters
- [ ] Write migration guide
- [ ] Update examples
- [ ] Add codemods for automatic migration

## Conclusion

This Zod-based schema-first approach represents a modern evolution of the TypeScript type registry system. By eliminating classes and embracing schemas, we achieve better type safety, runtime validation, and developer experience while maintaining full compatibility with Sekiban's event sourcing patterns.

The gradual migration path ensures existing projects can adopt this system incrementally, while new projects can start with a clean, modern architecture from day one.