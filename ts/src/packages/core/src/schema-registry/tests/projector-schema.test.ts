import { describe, it, expect } from 'vitest';
import { ok, err } from 'neverthrow';
import { defineProjector } from '../projector-schema';
import { defineEvent } from '../event-schema';
import { PartitionKeys } from '../../documents/partition-keys';
import { Aggregate } from '../../aggregates/aggregate';
import { EmptyAggregatePayload } from '../../aggregates/aggregate';
import { SortableUniqueId } from '../../documents/sortable-unique-id';
import { ValidationError } from '../../result/errors';
import type { ITypedAggregatePayload } from '../../aggregates/aggregate-projector';
import type { IEvent } from '../../events/event';
import { z } from 'zod';

// Test aggregate payload types
interface UserPayload extends ITypedAggregatePayload {
  readonly aggregateType: 'User';
  readonly userId: string;
  readonly name: string;
  readonly email: string;
  readonly createdAt: string;
}

interface UpdatedUserPayload extends ITypedAggregatePayload {
  readonly aggregateType: 'User';
  readonly userId: string;
  readonly name: string;
  readonly email: string;
  readonly createdAt: string;
  readonly updatedAt: string;
}

interface DeletedUserPayload extends ITypedAggregatePayload {
  readonly aggregateType: 'DeletedUser';
  readonly deletedAt: string;
}

type UserPayloadUnion = UserPayload | UpdatedUserPayload | DeletedUserPayload;

// Test events
const UserCreated = defineEvent({
  type: 'UserCreated',
  schema: z.object({
    userId: z.string(),
    name: z.string(),
    email: z.string().email(),
    createdAt: z.string().datetime()
  })
});

const UserUpdated = defineEvent({
  type: 'UserUpdated',
  schema: z.object({
    userId: z.string(),
    name: z.string().optional(),
    email: z.string().email().optional(),
    updatedAt: z.string().datetime()
  })
});

const UserDeleted = defineEvent({
  type: 'UserDeleted',
  schema: z.object({
    userId: z.string(),
    deletedAt: z.string().datetime()
  })
});

// Helper to create IEvent from event payload
function createEvent(eventType: string, payload: any, aggregateId: string = 'user-123'): IEvent {
  return {
    id: SortableUniqueId.generate().value,
    aggregateId,
    aggregateType: 'User',
    eventType,
    eventVersion: '1',
    sortableUniqueId: SortableUniqueId.generate(),
    payload,
    createdBy: 'test',
    createdAt: new Date()
  };
}

describe('defineProjector', () => {
  // Test 1: Basic projector definition
  it('creates projector with aggregateType', () => {
    // Arrange
    const definition = {
      aggregateType: 'User',
      initialState: () => new EmptyAggregatePayload(),
      projections: {}
    };

    // Act
    const projector = defineProjector<UserPayloadUnion>(definition);

    // Assert
    expect(projector.aggregateType).toBe('User');
  });

  // Test 2: getInitialState returns empty aggregate
  it('getInitialState returns empty aggregate', () => {
    // Arrange
    const userProjector = defineProjector<UserPayloadUnion>({
      aggregateType: 'User',
      initialState: () => new EmptyAggregatePayload(),
      projections: {}
    });
    const partitionKeys = PartitionKeys.generate('User');

    // Act
    const initialAggregate = userProjector.getInitialState(partitionKeys);

    // Assert
    expect(initialAggregate.payload.aggregateType).toBe('Empty');
    expect(initialAggregate.version).toBe(0);
    expect(initialAggregate.aggregateType).toBe('User');
  });

  // Test 3: project handles registered event types
  it('project handles registered event types', () => {
    // Arrange
    const userProjector = defineProjector<UserPayloadUnion>({
      aggregateType: 'User',
      initialState: () => new EmptyAggregatePayload(),
      projections: {
        UserCreated: (state, event) => ({
          aggregateType: 'User' as const,
          userId: event.userId,
          name: event.name,
          email: event.email,
          createdAt: event.createdAt
        })
      }
    });

    const emptyAggregate = new Aggregate<EmptyAggregatePayload>(
      PartitionKeys.generate('User'),
      'User',
      0,
      new EmptyAggregatePayload(),
      null,
      'User',
      1
    );

    const userCreatedEvent = createEvent('UserCreated', {
      userId: 'user-123',
      name: 'John Doe',
      email: 'john@example.com',
      createdAt: '2024-01-15T10:30:00Z'
    });

    // Act
    const result = userProjector.project(emptyAggregate, userCreatedEvent);

    // Assert
    expect(result.isOk()).toBe(true);
    if (result.isOk()) {
      const newAggregate = result.value;
      expect(newAggregate.payload.aggregateType).toBe('User');
      expect((newAggregate.payload as UserPayload).name).toBe('John Doe');
      expect(newAggregate.version).toBe(1);
    }
  });

  // Test 4: project ignores unregistered event types
  it('project ignores unregistered event types', () => {
    // Arrange
    const userProjector = defineProjector<UserPayloadUnion>({
      aggregateType: 'User',
      initialState: () => new EmptyAggregatePayload(),
      projections: {
        UserCreated: (state, event) => ({
          aggregateType: 'User' as const,
          userId: event.userId,
          name: event.name,
          email: event.email,
          createdAt: event.createdAt
        })
      }
    });

    const emptyAggregate = new Aggregate<EmptyAggregatePayload>(
      PartitionKeys.generate('User'),
      'User',
      0,
      new EmptyAggregatePayload(),
      null,
      'User',
      1
    );

    const unknownEvent = createEvent('UnknownEvent', { data: 'test' });

    // Act
    const result = userProjector.project(emptyAggregate, unknownEvent);

    // Assert
    expect(result.isOk()).toBe(true);
    if (result.isOk()) {
      const aggregate = result.value;
      expect(aggregate.payload.aggregateType).toBe('Empty'); // Unchanged
      expect(aggregate.version).toBe(0); // Version unchanged
    }
  });

  // Test 5: projections transform state correctly
  it('projections transform state correctly', () => {
    // Arrange
    const userProjector = defineProjector<UserPayloadUnion>({
      aggregateType: 'User',
      initialState: () => new EmptyAggregatePayload(),
      projections: {
        UserCreated: (state, event) => ({
          aggregateType: 'User' as const,
          userId: event.userId,
          name: event.name,
          email: event.email,
          createdAt: event.createdAt
        }),
        UserUpdated: (state, event) => {
          if (state.aggregateType !== 'User') return state;
          return {
            ...state,
            name: event.name || state.name,
            email: event.email || state.email,
            updatedAt: event.updatedAt
          };
        }
      }
    });

    // Create user first
    const emptyAggregate = new Aggregate<EmptyAggregatePayload>(
      PartitionKeys.generate('User'),
      'User',
      0,
      new EmptyAggregatePayload(),
      null,
      'User',
      1
    );

    const userCreatedEvent = createEvent('UserCreated', {
      userId: 'user-123',
      name: 'John Doe',
      email: 'john@example.com',
      createdAt: '2024-01-15T10:30:00Z'
    });

    const userAggregate = userProjector.project(emptyAggregate, userCreatedEvent);
    expect(userAggregate.isOk()).toBe(true);

    // Update user
    const userUpdatedEvent = createEvent('UserUpdated', {
      userId: 'user-123',
      name: 'John Smith',
      updatedAt: '2024-01-16T10:30:00Z'
    });

    // Act
    const result = userProjector.project(userAggregate.value!, userUpdatedEvent);

    // Assert
    expect(result.isOk()).toBe(true);
    if (result.isOk()) {
      const updatedAggregate = result.value;
      const payload = updatedAggregate.payload as UpdatedUserPayload;
      expect(payload.aggregateType).toBe('User');
      expect(payload.name).toBe('John Smith');
      expect(payload.email).toBe('john@example.com'); // Unchanged
      expect(payload.updatedAt).toBe('2024-01-16T10:30:00Z');
    }
  });

  // Test 6: handles state type transitions
  it('handles state type transitions', () => {
    // Arrange
    const userProjector = defineProjector<UserPayloadUnion>({
      aggregateType: 'User',
      initialState: () => new EmptyAggregatePayload(),
      projections: {
        UserCreated: (state, event) => ({
          aggregateType: 'User' as const,
          userId: event.userId,
          name: event.name,
          email: event.email,
          createdAt: event.createdAt
        }),
        UserDeleted: (state, event) => ({
          aggregateType: 'DeletedUser' as const,
          deletedAt: event.deletedAt
        })
      }
    });

    // Create user first
    const emptyAggregate = new Aggregate<EmptyAggregatePayload>(
      PartitionKeys.generate('User'),
      'User',
      0,
      new EmptyAggregatePayload(),
      null,
      'User',
      1
    );

    const userCreatedEvent = createEvent('UserCreated', {
      userId: 'user-123',
      name: 'John Doe',
      email: 'john@example.com',
      createdAt: '2024-01-15T10:30:00Z'
    });

    const userAggregate = userProjector.project(emptyAggregate, userCreatedEvent);
    expect(userAggregate.isOk()).toBe(true);

    // Delete user (state transition)
    const userDeletedEvent = createEvent('UserDeleted', {
      userId: 'user-123',
      deletedAt: '2024-01-16T10:30:00Z'
    });

    // Act
    const result = userProjector.project(userAggregate.value!, userDeletedEvent);

    // Assert
    expect(result.isOk()).toBe(true);
    if (result.isOk()) {
      const deletedAggregate = result.value;
      const payload = deletedAggregate.payload as DeletedUserPayload;
      expect(payload.aggregateType).toBe('DeletedUser');
      expect(payload.deletedAt).toBe('2024-01-16T10:30:00Z');
    }
  });

  // Test 7: project increments version
  it('project increments version', () => {
    // Arrange
    const userProjector = defineProjector<UserPayloadUnion>({
      aggregateType: 'User',
      initialState: () => new EmptyAggregatePayload(),
      projections: {
        UserCreated: (state, event) => ({
          aggregateType: 'User' as const,
          userId: event.userId,
          name: event.name,
          email: event.email,
          createdAt: event.createdAt
        })
      }
    });

    const emptyAggregate = new Aggregate<EmptyAggregatePayload>(
      PartitionKeys.generate('User'),
      'User',
      0,
      new EmptyAggregatePayload(),
      null,
      'User',
      1
    );

    const userCreatedEvent = createEvent('UserCreated', {
      userId: 'user-123',
      name: 'John Doe',
      email: 'john@example.com',
      createdAt: '2024-01-15T10:30:00Z'
    });

    // Act
    const result = userProjector.project(emptyAggregate, userCreatedEvent);

    // Assert
    expect(result.isOk()).toBe(true);
    if (result.isOk()) {
      const newAggregate = result.value;
      expect(newAggregate.version).toBe(1); // Incremented from 0
      expect(newAggregate.lastSortableUniqueId).toBeDefined();
    }
  });

  // Test 8: project handles projection errors
  it('project handles projection errors', () => {
    // Arrange
    const faultyProjector = defineProjector<UserPayloadUnion>({
      aggregateType: 'User',
      initialState: () => new EmptyAggregatePayload(),
      projections: {
        UserCreated: (state, event) => {
          throw new Error('Projection failed');
        }
      }
    });

    const emptyAggregate = new Aggregate<EmptyAggregatePayload>(
      PartitionKeys.generate('User'),
      'User',
      0,
      new EmptyAggregatePayload(),
      null,
      'User',
      1
    );

    const userCreatedEvent = createEvent('UserCreated', {
      userId: 'user-123',
      name: 'John Doe',
      email: 'john@example.com',
      createdAt: '2024-01-15T10:30:00Z'
    });

    // Act
    const result = faultyProjector.project(emptyAggregate, userCreatedEvent);

    // Assert
    expect(result.isErr()).toBe(true);
    if (result.isErr()) {
      expect(result.error).toBeInstanceOf(ValidationError);
      expect(result.error.message).toContain('Projection failed');
    }
  });

  // Test 9: Complex projector with multiple state types
  it('handles complex projector with multiple state types', () => {
    // Arrange
    interface DraftOrderPayload extends ITypedAggregatePayload {
      readonly aggregateType: 'DraftOrder';
      readonly orderId: string;
      readonly items: Array<{ productId: string; quantity: number }>;
    }

    interface ConfirmedOrderPayload extends ITypedAggregatePayload {
      readonly aggregateType: 'ConfirmedOrder';
      readonly orderId: string;
      readonly items: Array<{ productId: string; quantity: number }>;
      readonly confirmedAt: string;
    }

    interface CancelledOrderPayload extends ITypedAggregatePayload {
      readonly aggregateType: 'CancelledOrder';
      readonly orderId: string;
      readonly cancelledAt: string;
      readonly reason: string;
    }

    type OrderPayloadUnion = DraftOrderPayload | ConfirmedOrderPayload | CancelledOrderPayload;

    const orderProjector = defineProjector<OrderPayloadUnion>({
      aggregateType: 'Order',
      initialState: () => new EmptyAggregatePayload(),
      projections: {
        OrderCreated: (state, event) => ({
          aggregateType: 'DraftOrder' as const,
          orderId: event.orderId,
          items: event.items
        }),
        OrderConfirmed: (state, event) => {
          if (state.aggregateType !== 'DraftOrder') return state;
          return {
            aggregateType: 'ConfirmedOrder' as const,
            orderId: state.orderId,
            items: state.items,
            confirmedAt: event.confirmedAt
          };
        },
        OrderCancelled: (state, event) => ({
          aggregateType: 'CancelledOrder' as const,
          orderId: event.orderId,
          cancelledAt: event.cancelledAt,
          reason: event.reason
        })
      }
    });

    const emptyAggregate = new Aggregate<EmptyAggregatePayload>(
      PartitionKeys.generate('Order'),
      'Order',
      0,
      new EmptyAggregatePayload(),
      null,
      'Order',
      1
    );

    // Create order
    const orderCreatedEvent = createEvent('OrderCreated', {
      orderId: 'order-123',
      items: [{ productId: 'prod1', quantity: 2 }]
    });

    const draftResult = orderProjector.project(emptyAggregate, orderCreatedEvent);
    expect(draftResult.isOk()).toBe(true);

    // Confirm order
    const orderConfirmedEvent = createEvent('OrderConfirmed', {
      orderId: 'order-123',
      confirmedAt: '2024-01-15T10:30:00Z'
    });

    // Act
    const confirmedResult = orderProjector.project(draftResult.value!, orderConfirmedEvent);

    // Assert
    expect(confirmedResult.isOk()).toBe(true);
    if (confirmedResult.isOk()) {
      const payload = confirmedResult.value.payload as ConfirmedOrderPayload;
      expect(payload.aggregateType).toBe('ConfirmedOrder');
      expect(payload.orderId).toBe('order-123');
      expect(payload.items).toHaveLength(1);
      expect(payload.confirmedAt).toBe('2024-01-15T10:30:00Z');
    }
  });

  // Test 10: TypeScript type inference works correctly
  it('provides correct TypeScript types through inference', () => {
    // Arrange
    const userProjector = defineProjector<UserPayloadUnion>({
      aggregateType: 'User',
      initialState: () => new EmptyAggregatePayload(),
      projections: {
        UserCreated: (state, event) => {
          // TypeScript should infer that state is EmptyAggregatePayload | UserPayloadUnion
          // and event is the event payload
          return {
            aggregateType: 'User' as const,
            userId: event.userId,
            name: event.name,
            email: event.email,
            createdAt: event.createdAt
          };
        }
      }
    });

    // Act & Assert - TypeScript compile-time check
    const aggregateType: string = userProjector.aggregateType;
    expect(aggregateType).toBe('User');

    // The projector should work with the correct types
    expect(typeof userProjector.getInitialState).toBe('function');
    expect(typeof userProjector.project).toBe('function');
  });
});