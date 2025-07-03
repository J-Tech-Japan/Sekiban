# @sekiban/core

Core event sourcing and CQRS framework for TypeScript.

## Phase 1 Implementation Status ✅

Successfully implemented using t_wada's TDD approach (Red-Green-Refactor):

### 1.1 Date Producer ✓
- `ISekibanDateProducer` interface
- `SekibanDateProducer` implementation with singleton pattern
- Mock implementations for testing
- Full test coverage

### 1.2 UUID Extensions ✓
- `createVersion7()` - Time-ordered UUID v7 implementation
- `createNamespacedUuid()` - Deterministic UUID generation
- `generateUuid()` - Standard UUID v4
- `isValidUuid()` - UUID validation
- Full test coverage including edge cases

### 1.3 Validation Utilities ✓
- Zod-based validation framework
- `createValidator()` - Create validators from Zod schemas
- `isValid()`, `getErrors()`, `validateOrThrow()` helper functions
- Support for complex validation scenarios
- Full test coverage

## Usage

```typescript
import { 
  SekibanDateProducer,
  createVersion7,
  createValidator,
  z
} from '@sekiban/core';

// Date producer
const dateProducer = SekibanDateProducer.getRegistered();
const now = dateProducer.now();

// UUID v7 (time-ordered)
const aggregateId = createVersion7();

// Validation
const userSchema = z.object({
  name: z.string().min(1),
  email: z.string().email()
});

const validator = createValidator(userSchema);
const result = validator.validate({ name: 'John', email: 'john@example.com' });

if (result.success) {
  console.log('Valid user:', result.data);
}
```

## Testing

```bash
pnpm test
```

## Building

```bash
pnpm build
```