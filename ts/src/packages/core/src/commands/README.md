# Commands Module

This module contains command execution utilities for Sekiban TypeScript.

## Command Definition

Commands are defined using the schema-based approach from the `schema-registry` module:

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

## Module Contents

- `unified-executor.ts` - Command executor that works with schema-based commands
- `validation.ts` - Validation utilities for command input
- `types.ts` - Basic types for command execution options

## See Also

- `/src/schema-registry/` - For command definition
- `/src/schema-registry/examples/` - For usage examples