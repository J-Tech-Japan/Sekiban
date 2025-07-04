# Sekiban Saga Package

Enterprise-grade Process Managers and Sagas for TypeScript Event Sourcing applications.

## Overview

The Saga package provides comprehensive support for managing long-running business processes using both **Orchestration** and **Choreography** patterns. It includes production-ready persistence, error handling, compensation, and comprehensive monitoring capabilities.

## Features

### 🎭 **Dual Pattern Support**
- **Orchestration**: Centralized control with step-by-step execution
- **Choreography**: Distributed coordination through event reactions

### 💾 **Production-Ready Persistence**  
- **In-Memory Repository**: Fast, perfect for testing and development
- **JSON File Repository**: Simple file-based persistence with atomic operations
- **Pluggable Architecture**: Easy to add database adapters (PostgreSQL, MongoDB, etc.)

### 🔄 **Advanced Error Handling**
- **Compensation Strategies**: Backward, Forward, Parallel, Custom
- **Retry Policies**: Exponential backoff, circuit breakers
- **Timeout Management**: Automatic saga timeout with cleanup

### 🎯 **Event Correlation**
- Smart event correlation for choreography patterns
- Time-window based correlation
- Policy-based reaction limiting

### 📊 **Observability**
- Comprehensive event logging
- Status tracking and monitoring
- Performance metrics

## Quick Start

### Installation

```bash
npm install @sekiban/saga
```

### Basic Orchestration Saga

```typescript
import { 
  SagaDefinition, 
  SagaManager, 
  CompensationStrategy,
  createSagaStore,
  InMemorySagaRepository 
} from '@sekiban/saga';

// Define your saga context
interface OrderContext {
  orderId: string;
  customerId: string;
  amount: number;
  paymentId?: string;
}

// Create saga definition
const OrderSaga: SagaDefinition<OrderContext> = {
  name: 'OrderProcessingSaga',
  version: 1,
  trigger: {
    eventType: 'OrderPlaced',
    filter: (event) => (event.payload as any).amount > 0
  },
  initialContext: (trigger) => ({
    orderId: trigger.payload.orderId,
    customerId: trigger.payload.customerId,
    amount: trigger.payload.amount
  }),
  steps: [
    {
      name: 'ProcessPayment',
      command: (context) => ({
        type: 'ProcessPayment',
        payload: {
          orderId: context.orderId,
          amount: context.amount
        }
      }),
      onSuccess: (context, event) => ({
        ...context,
        paymentId: event.payload.paymentId
      }),
      compensation: (context) => ({
        type: 'RefundPayment',
        payload: { paymentId: context.paymentId! }
      }),
      retryPolicy: {
        maxAttempts: 3,
        backoffMs: 1000,
        exponential: true
      }
    }
  ],
  compensationStrategy: CompensationStrategy.Backward,
  timeout: 300000 // 5 minutes
};

// Setup persistence and saga manager
const repository = new InMemorySagaRepository();
const sagaStore = createSagaStore(repository);
const sagaManager = new SagaManager({
  commandExecutor: myCommandExecutor,
  sagaStore
});

// Register and use
sagaManager.register(OrderSaga);
await sagaManager.handleEvent(orderPlacedEvent);
```

### Basic Choreography Saga

```typescript
import { 
  ChoreographySaga, 
  SagaCoordinator,
  ReactionCondition 
} from '@sekiban/saga';

const OrderChoreography: ChoreographySaga = {
  name: 'OrderProcessingChoreography',
  version: 1,
  reactions: [
    {
      name: 'ProcessPaymentOnOrderPlaced',
      trigger: {
        eventType: 'OrderPlaced',
        condition: ReactionCondition.hasAmount(0)
      },
      action: {
        type: 'command',
        command: (event) => ({
          type: 'ProcessPayment',
          payload: {
            orderId: event.payload.orderId,
            amount: event.payload.amount
          }
        })
      }
    },
    {
      name: 'CompleteOrderOnPaymentSuccess',
      trigger: {
        eventType: 'PaymentProcessed',
        correlation: {
          key: 'orderId',
          requires: ['OrderPlaced'],
          within: 300000 // 5 minutes
        }
      },
      action: {
        type: 'event',
        event: (trigger) => ({
          eventType: 'OrderCompleted',
          payload: {
            orderId: trigger.payload.orderId,
            completedAt: new Date()
          }
        })
      }
    }
  ]
};

// Setup coordinator
const coordinator = new SagaCoordinator({
  eventStore: myEventStore,
  commandExecutor: myCommandExecutor
});

coordinator.register(OrderChoreography);
await coordinator.handleEvent(orderPlacedEvent);
```

## Persistence Options

### In-Memory Repository

Perfect for testing and development:

```typescript
import { InMemorySagaRepository } from '@sekiban/saga';

const repository = new InMemorySagaRepository({
  enableAutoCleanup: true,
  cleanupIntervalMs: 60000 // 1 minute
});
```

### JSON File Repository

Simple file-based persistence:

```typescript
import { JsonFileSagaRepository } from '@sekiban/saga';

const repository = new JsonFileSagaRepository({
  dataDirectory: './saga-data',
  prettyPrint: true,
  enableAutoCleanup: true,
  enableFileLocking: true
});
```

### Custom Repository

Implement the `SagaRepository` interface for your database:

```typescript
import { SagaRepository, SagaSnapshot } from '@sekiban/saga';

class PostgresSagaRepository implements SagaRepository {
  async load(id: string): Promise<SagaSnapshot | null> {
    // Your PostgreSQL implementation
  }
  
  async save(snapshot: SagaSnapshot): Promise<void> {
    // Your PostgreSQL implementation with optimistic locking
  }
  
  // ... implement other methods
}
```

## Advanced Features

### Compensation Strategies

```typescript
import { CompensationStrategy } from '@sekiban/saga';

const saga: SagaDefinition<MyContext> = {
  // ...
  compensationStrategy: CompensationStrategy.Backward, // Reverse order
  // or CompensationStrategy.Forward,   // Same order
  // or CompensationStrategy.Parallel,  // All at once
  // or CompensationStrategy.Custom     // Custom logic
};
```

### Retry Policies

```typescript
const stepWithRetry = {
  name: 'ReliableStep',
  command: (context) => myCommand(context),
  retryPolicy: {
    maxAttempts: 5,
    backoffMs: 1000,
    exponential: true,
    maxBackoffMs: 30000
  },
  onSuccess: (context, event) => ({ ...context, stepCompleted: true })
};
```

### Event Correlation

```typescript
const correlatedReaction = {
  name: 'ComplexCorrelation',
  trigger: {
    eventType: 'FinalEvent',
    correlation: {
      key: 'transactionId',
      requires: ['InitialEvent', 'MiddleEvent'],
      within: 600000 // 10 minutes
    }
  },
  action: {
    type: 'command',
    command: (event) => createFinalCommand(event)
  }
};
```

### Policy-Based Reactions

```typescript
const limitedReaction = {
  name: 'RetryWithLimit',
  trigger: {
    eventType: 'FailureEvent',
    policy: {
      maxOccurrences: 3,
      window: 3600000 // 1 hour
    }
  },
  action: {
    type: 'command',
    command: (event) => createRetryCommand(event)
  }
};
```

### Timeout Handling

```typescript
const timeoutReaction = {
  name: 'TimeoutHandler',
  trigger: {
    eventType: 'ProcessStarted',
    timeout: {
      duration: 300000, // 5 minutes
      action: {
        type: 'event',
        event: (trigger) => ({
          eventType: 'ProcessTimedOut',
          payload: { processId: trigger.payload.processId }
        })
      },
      unless: ['ProcessCompleted', 'ProcessCancelled']
    }
  },
  action: {
    type: 'command',
    command: (event) => startMonitoring(event)
  }
};
```

## Error Handling

The saga system provides comprehensive error handling:

```typescript
import { 
  SagaError, 
  SagaNotFoundError, 
  SagaTimeoutError,
  SagaConcurrencyError 
} from '@sekiban/saga';

try {
  await sagaManager.executeNextStep(sagaId);
} catch (error) {
  if (error instanceof SagaTimeoutError) {
    console.log('Saga timed out:', error.sagaId);
  } else if (error instanceof SagaConcurrencyError) {
    console.log('Concurrent modification detected');
  }
}
```

## Monitoring and Observability

### Query Saga Status

```typescript
// List running sagas
const runningSagas = await repository.findByStatus('running');

// Find expired sagas
const expired = await repository.findExpired(new Date());

// Filter sagas
const recentSagas = await repository.list({
  sagaType: 'OrderProcessingSaga',
  createdAfter: new Date(Date.now() - 3600000), // Last hour
  limit: 100
});
```

### Saga Events

```typescript
const sagaStore = createSagaStore(repository);

// Events are automatically saved during saga execution
const events = sagaStore.getEvents(sagaId);
console.log('Saga events:', events);
```

## Testing

The package includes comprehensive testing utilities:

```typescript
import { 
  InMemorySagaRepository,
  createSagaRepositoryContractTests 
} from '@sekiban/saga';

// Test your custom repository implementation
describe('My Custom Repository', createSagaRepositoryContractTests(
  async () => new MyCustomRepository(),
  async (repo) => repo.cleanup()
));

// Test saga behavior
describe('Order Saga', () => {
  let sagaManager: SagaManager;
  let mockRepository: InMemorySagaRepository;

  beforeEach(() => {
    mockRepository = new InMemorySagaRepository();
    const sagaStore = createSagaStore(mockRepository);
    sagaManager = new SagaManager({
      commandExecutor: mockCommandExecutor,
      sagaStore
    });
    sagaManager.register(OrderSaga);
  });

  it('should process order successfully', async () => {
    await sagaManager.handleEvent(orderPlacedEvent);
    await sagaManager.executeNextStep(sagaId);
    
    const instance = await sagaStore.load(sagaId);
    expect(instance.value?.state.status).toBe(SagaStatus.Completed);
  });
});
```

## Production Considerations

### Performance

- Use appropriate repository implementations for your scale
- Configure cleanup intervals based on your saga lifecycle
- Monitor saga execution times and set appropriate timeouts

### Reliability

- Always use optimistic concurrency control for saga state
- Implement proper retry policies for transient failures
- Use compensation strategies appropriate for your business logic

### Monitoring

- Track saga success/failure rates
- Monitor saga execution durations
- Set up alerts for stuck or expired sagas

## Examples

See the [examples](./examples) directory for complete working examples:

- **Order Fulfillment**: Complete e-commerce order processing
- **Payment Processing**: Multi-step payment with 3DS
- **Data Migration**: Long-running data transformation saga

## API Reference

### Core Interfaces

- `SagaDefinition<TContext>` - Orchestration saga definition
- `ChoreographySaga` - Choreography saga definition  
- `SagaRepository<TState>` - Persistence interface
- `SagaManager` - Orchestration execution engine
- `SagaCoordinator` - Choreography execution engine

### Utilities

- `SagaSnapshotUtils` - Helper functions for saga snapshots
- `ReactionCondition` - Pre-built reaction conditions
- `createSagaStore()` - Factory for saga store adapter

For detailed API documentation, see the TypeScript definitions in the source code.

## License

MIT