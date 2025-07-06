# Commands Module

This module contains command-related functionality for Sekiban TypeScript.

## ⚠️ Important: Use Schema-Based Commands

**For new development, please use the schema-based command approach from the `schema-registry` module.**

The interfaces and classes in this directory are maintained for backward compatibility but are considered legacy.

## Recommended Approach

Use `defineCommand` from schema-registry:

```typescript
import { defineCommand } from '@sekiban/core/schema-registry';
import { TypedPartitionKeys } from '@sekiban/core/documents';

const CreateUser = defineCommand({
  type: 'CreateUser',
  schema: z.object({
    name: z.string(),
    email: z.string()
  }),
  projector: UserProjector, // Required - aligns with C# design
  handlers: {
    specifyPartitionKeys: (data) => TypedPartitionKeys.Generate(UserProjector),
    handle: (data, context) => {
      // Context-based handling
      return ok([UserCreated.create({ ... })]);
    }
  }
});
```

## Migration

See `/src/schema-registry/MIGRATION_GUIDE.md` for detailed migration instructions.

## Legacy Interfaces (Deprecated)

- `ICommand<TPayloadUnion>` - Old command interface without projector
- `ICommandWithHandler<TCommand, TPayload>` - Old handler interface without context
- These are kept for backward compatibility only

## Current Module Contents

- `unified-executor.ts` - New executor that works with schema-based commands
- `executor-adapter.ts` - Adapter for backward compatibility
- Legacy files maintained for existing code