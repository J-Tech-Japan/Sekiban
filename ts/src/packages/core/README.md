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

### Phase 3: Basic Interfaces ✅

#### 3.1 Event Basic Types ✓
- `IEventPayload` - Marker interface for event payloads
- `IAggregatePayload` - Marker interface for aggregate states
- Type guards for runtime validation
- Full test coverage (17 tests)

#### 3.2 Event Type Definitions ✓
- `IEvent<TPayload>` - Core event interface
- `Event` class with immutability
- `EventMetadata` with correlation/causation support
- Event creation helpers
- Full test coverage (10 tests)

### Phase 4: Event Management ✅

#### 4.1 Event Document ✓
- `EventDocument` - Wrapper for events with convenience accessors
- `SerializableEventDocument` - JSON-serializable event representation
- Serialization/deserialization utilities
- Full test coverage (9 tests)

#### 4.2 In-Memory Event Store ✓
- `InMemoryEventStore` - Event storage implementation
- `IEventReader` & `IEventWriter` interfaces
- Version consistency enforcement
- Multi-tenant support
- Full test coverage (13 tests)

### Phase 5: Exception and Error Handling ✅

#### 5.1 Sekiban Error Classes ✓
- Base `SekibanError` class with serialization support
- Domain-specific error types (10 error classes)
- Full error inheritance hierarchy
- JSON serialization support
- Full test coverage (20 tests)

#### 5.2 Error Utilities ✓
- `toResult()` - Convert promises to Results
- `fromThrowable()` - Wrap throwing functions
- `mapError()` - Transform error types
- `collectErrors()` - Gather errors from Results
- `chainErrors()` - Create error cause chains
- Full test coverage (15 tests)

#### 5.3 Error Type Guards ✓
- Type-safe error checking functions
- Support for all Sekiban error types
- TypeScript type narrowing
- Composable guards
- Full test coverage (13 tests)

### Phase 6: Aggregates and Projectors ✅

#### 6.1 Aggregate System ✓
- `IAggregate` interface with version tracking
- `Aggregate` class with immutability
- Empty aggregate support
- Version management and event tracking
- Full test coverage (8 tests)

#### 6.2 Projector System ✓
- `IAggregateProjector` - Pure projection functions
- `IProjector` - Extended with type information
- Pattern matching for state transitions
- Event-driven state updates
- Full test coverage (9 tests)

#### 6.3 Aggregate Projector ✓
- Wrapper for applying projectors to aggregates
- Sequential event application
- Initial aggregate creation
- Error handling
- Full test coverage (6 tests)

### Phase 7: Command Handling ✅

#### 7.1 Command Interfaces ✓
- `ICommand` marker interface
- `ICommandHandler` for command processing
- `ICommandWithHandler` combining command and handler
- Command context with aggregate access
- Full test coverage (7 tests)

#### 7.2 Command Validation ✓
- Declarative validation rules
- Built-in validators (required, minLength, email, etc.)
- Custom validation support
- Validation error collection
- Full test coverage (9 tests)

**Total Tests: 226 passing** ✅

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

// Error handling with neverthrow
import { toResult, isEventStoreError } from '@sekiban/core';

const result = await toResult(fetchData());
if (result.isErr()) {
  if (isEventStoreError(result.error)) {
    console.error('Event store error:', result.error.operation);
  }
}

// Aggregates and Projectors
import { 
  IProjector, 
  IAggregatePayload, 
  IEventPayload,
  AggregateProjector,
  createEmptyAggregate
} from '@sekiban/core';

// Define your projector
class AccountProjector implements IProjector<IAggregatePayload> {
  getTypeName() { return 'AccountProjector'; }
  getVersion() { return 1; }
  
  project(state: IAggregatePayload, event: IEventPayload): IAggregatePayload {
    // Pattern matching logic here
    return state;
  }
}

// Use the projector
const projector = new AggregateProjector(new AccountProjector());
const aggregate = projector.getInitialAggregate(partitionKeys, 'Account');

// Commands
import { 
  ICommandWithHandler,
  validateCommand,
  required,
  email as emailValidator
} from '@sekiban/core';

class CreateUserCommand implements ICommandWithHandler<CreateUserCommand, UserProjector> {
  constructor(
    public readonly name: string,
    public readonly email: string
  ) {}
  
  validate() {
    return validateCommand(this, {
      name: [required('Name is required')],
      email: [required('Email is required'), emailValidator('Invalid email')]
    });
  }
  
  getPartitionKeys() {
    return PartitionKeys.generate('users');
  }
  
  handle(command: CreateUserCommand, context: ICommandContextWithoutState) {
    const event = new UserCreated(command.name, command.email);
    return ok(EventOrNone.event(context.createEvent(event)));
  }
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