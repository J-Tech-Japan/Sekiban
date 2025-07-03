# @sekiban/core

Core event sourcing and CQRS framework for TypeScript.

## Implementation Status

Successfully implemented using t_wada's TDD approach (Red-Green-Refactor).

### Phase 1: Base Utilities ✅

#### 1.1 Date Producer ✓
- `ISekibanDateProducer` interface
- `SekibanDateProducer` implementation with singleton pattern
- Mock implementations for testing
- Full test coverage (9 tests)

#### 1.2 UUID Extensions ✓
- `createVersion7()` - Time-ordered UUID v7 implementation
- `createNamespacedUuid()` - Deterministic UUID generation
- `generateUuid()` - Standard UUID v4
- `isValidUuid()` - UUID validation
- Full test coverage including edge cases (19 tests)

#### 1.3 Validation Utilities ✓
- Zod-based validation framework
- `createValidator()` - Create validators from Zod schemas
- `isValid()`, `getErrors()`, `validateOrThrow()` helper functions
- Support for complex validation scenarios
- Full test coverage (17 tests)

### Phase 2: Core Document Types ✅

#### 2.1 SortableUniqueId ✓
- Time-ordered unique identifier for events
- Sortable string representation
- Counter for rapid generation
- Result-based error handling
- Full test coverage (8 tests)

#### 2.2 PartitionKeys ✓
- Aggregate ID management
- Group and multi-tenant support
- Composite key generation
- Equality comparison
- Full test coverage (13 tests)

#### 2.3 Metadata ✓
- Command and event metadata
- User ID, correlation ID, causation ID tracking
- Custom metadata support
- Builder pattern implementation
- Full test coverage (24 tests)

**Total Tests: 90 passing** ✅

## Usage

```typescript
import { 
  SekibanDateProducer,
  createVersion7,
  createValidator,
  SortableUniqueId,
  PartitionKeys,
  Metadata,
  MetadataBuilder,
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

// Sortable Unique ID
const eventId = SortableUniqueId.generate();
console.log('Event ID:', eventId.toString());

// Partition Keys
const partitionKeys = PartitionKeys.create('user-123', 'users', 'tenant-1');
console.log('Partition Key:', partitionKeys.toString()); // "tenant-1-users-user-123"

// Metadata
const metadata = new MetadataBuilder()
  .withUserId('user-123')
  .withCorrelationId('request-456')
  .withCustom('source', 'web-api')
  .build();
```

## Testing

```bash
pnpm test
```

## Building

```bash
pnpm build
```