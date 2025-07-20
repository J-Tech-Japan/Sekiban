import { describe, it, expect } from 'vitest';
import { ok, err } from 'neverthrow';
import { AggregateProjector, type ITypedAggregatePayload, type EmptyAggregatePayload } from './aggregate-projector';
import type { IEventPayload } from '../events/event-payload';
import { Aggregate } from './aggregate';
import type { SekibanError } from '../result/errors';
import { SortableUniqueId } from '../documents/sortable-unique-id';
import { PartitionKeys } from '../documents/partition-keys';

// Test domain - User aggregate with multiple states
interface UnconfirmedUserPayload extends ITypedAggregatePayload {
  readonly aggregateType: 'UnconfirmedUser';
  id: string;
  name: string;
  email: string;
  createdAt: string;
}

interface ConfirmedUserPayload extends ITypedAggregatePayload {
  readonly aggregateType: 'ConfirmedUser';
  id: string;
  name: string;
  email: string;
  createdAt: string;
  confirmedAt: string;
}

type UserPayloadUnion = UnconfirmedUserPayload | ConfirmedUserPayload;

// Test events
interface UserRegisteredEvent extends IEventPayload {
  readonly eventType: 'UserRegistered';
  userId: string;
  name: string;
  email: string;
}

interface UserConfirmedEvent extends IEventPayload {
  readonly eventType: 'UserConfirmed';
  userId: string;
  confirmedAt: string;
}

// Test projector
class TestUserProjector extends AggregateProjector<UserPayloadUnion> {
  readonly aggregateTypeName = 'User';
  
  project(
    aggregate: Aggregate<UserPayloadUnion | EmptyAggregatePayload>,
    event: IEventPayload
  ) {
    switch (event.eventType) {
      case 'UserRegistered': {
        if (!this.isEmpty(aggregate.payload)) {
          return err({
            type: 'ProjectionError',
            message: 'User already exists'
          } as SekibanError);
        }
        
        const userEvent = event as UserRegisteredEvent;
        const newPayload: UnconfirmedUserPayload = {
          aggregateType: 'UnconfirmedUser',
          id: userEvent.userId,
          name: userEvent.name,
          email: userEvent.email,
          createdAt: new Date().toISOString()
        };
        
        return ok(this.createUpdatedAggregate(aggregate, newPayload, event));
      }
      
      case 'UserConfirmed': {
        if (!this.isPayloadType<UnconfirmedUserPayload>(aggregate.payload, 'UnconfirmedUser')) {
          return err({
            type: 'ProjectionError',
            message: 'Can only confirm unconfirmed users'
          } as SekibanError);
        }
        
        const confirmEvent = event as UserConfirmedEvent;
        const newPayload: ConfirmedUserPayload = {
          aggregateType: 'ConfirmedUser',
          id: aggregate.payload.id,
          name: aggregate.payload.name,
          email: aggregate.payload.email,
          createdAt: aggregate.payload.createdAt,
          confirmedAt: confirmEvent.confirmedAt
        };
        
        return ok(this.createUpdatedAggregate(aggregate, newPayload, event));
      }
      
      default:
        return ok(aggregate);
    }
  }
  
  canHandle(eventType: string): boolean {
    return ['UserRegistered', 'UserConfirmed'].includes(eventType);
  }
  
  getSupportedPayloadTypes(): string[] {
    return ['UnconfirmedUser', 'ConfirmedUser'];
  }
}

describe('MultiPayloadProjector', () => {
  describe('State Machine Pattern', () => {
    it('should create UnconfirmedUser from Empty state when UserRegistered', () => {
      // Arrange
      const projector = new TestUserProjector();
      const partitionKeys = new PartitionKeys('user-123', 'User', 'default');
      const emptyAggregate = projector.getInitialState(partitionKeys);
      
      const userRegisteredEvent: UserRegisteredEvent = {
        eventType: 'UserRegistered',
        userId: 'user-123',
        name: 'John Doe',
        email: 'john@example.com'
      };
      
      // Act
      const result = projector.project(emptyAggregate, userRegisteredEvent);
      
      // Assert
      expect(result.isOk()).toBe(true);
      if (result.isOk()) {
        const updatedAggregate = result.value;
        expect(updatedAggregate.payload.aggregateType).toBe('UnconfirmedUser');
        expect(projector.isPayloadType<UnconfirmedUserPayload>(updatedAggregate.payload, 'UnconfirmedUser')).toBe(true);
        
        if (projector.isPayloadType<UnconfirmedUserPayload>(updatedAggregate.payload, 'UnconfirmedUser')) {
          expect(updatedAggregate.payload.id).toBe('user-123');
          expect(updatedAggregate.payload.name).toBe('John Doe');
          expect(updatedAggregate.payload.email).toBe('john@example.com');
        }
      }
    });
    
    it('should transition from UnconfirmedUser to ConfirmedUser when UserConfirmed', () => {
      // Arrange
      const projector = new TestUserProjector();
      const partitionKeys = new PartitionKeys('user-123', 'User', 'default');
      
      // Create UnconfirmedUser state
      const unconfirmedUser: UnconfirmedUserPayload = {
        aggregateType: 'UnconfirmedUser',
        id: 'user-123',
        name: 'John Doe',
        email: 'john@example.com',
        createdAt: '2025-07-03T10:00:00.000Z'
      };
      
      const aggregateWithUnconfirmedUser = new Aggregate<UnconfirmedUserPayload>(
        partitionKeys,
        'User',
        1,
        unconfirmedUser,
        new SortableUniqueId('event-1'),
        'TestUserProjector',
        1
      );
      
      const userConfirmedEvent: UserConfirmedEvent = {
        eventType: 'UserConfirmed',
        userId: 'user-123',
        confirmedAt: '2025-07-03T11:00:00.000Z'
      };
      
      // Act
      const result = projector.project(aggregateWithUnconfirmedUser, userConfirmedEvent);
      
      // Assert
      expect(result.isOk()).toBe(true);
      if (result.isOk()) {
        const updatedAggregate = result.value;
        expect(updatedAggregate.payload.aggregateType).toBe('ConfirmedUser');
        expect(projector.isPayloadType<ConfirmedUserPayload>(updatedAggregate.payload, 'ConfirmedUser')).toBe(true);
        
        if (projector.isPayloadType<ConfirmedUserPayload>(updatedAggregate.payload, 'ConfirmedUser')) {
          expect(updatedAggregate.payload.id).toBe('user-123');
          expect(updatedAggregate.payload.name).toBe('John Doe');
          expect(updatedAggregate.payload.email).toBe('john@example.com');
          expect(updatedAggregate.payload.createdAt).toBe('2025-07-03T10:00:00.000Z');
          expect(updatedAggregate.payload.confirmedAt).toBe('2025-07-03T11:00:00.000Z');
        }
      }
    });
    
    it('should fail when trying to confirm already confirmed user', () => {
      // Arrange
      const projector = new TestUserProjector();
      const partitionKeys = new PartitionKeys('user-123', 'User', 'default');
      
      // Create ConfirmedUser state
      const confirmedUser: ConfirmedUserPayload = {
        aggregateType: 'ConfirmedUser',
        id: 'user-123',
        name: 'John Doe',
        email: 'john@example.com',
        createdAt: '2025-07-03T10:00:00.000Z',
        confirmedAt: '2025-07-03T11:00:00.000Z'
      };
      
      const aggregateWithConfirmedUser = new Aggregate<ConfirmedUserPayload>(
        partitionKeys,
        'User',
        2,
        confirmedUser,
        new SortableUniqueId('event-2'),
        'TestUserProjector',
        1
      );
      
      const userConfirmedEvent: UserConfirmedEvent = {
        eventType: 'UserConfirmed',
        userId: 'user-123',
        confirmedAt: '2025-07-03T12:00:00.000Z'
      };
      
      // Act
      const result = projector.project(aggregateWithConfirmedUser, userConfirmedEvent);
      
      // Assert
      expect(result.isErr()).toBe(true);
      if (result.isErr()) {
        expect(result.error.message).toContain('Can only confirm unconfirmed users');
      }
    });
    
    it('should provide correct metadata about supported types', () => {
      // Arrange
      const projector = new TestUserProjector();
      
      // Act & Assert
      expect(projector.aggregateTypeName).toBe('User');
      expect(projector.getSupportedPayloadTypes()).toEqual(['UnconfirmedUser', 'ConfirmedUser']);
      expect(projector.canHandle('UserRegistered')).toBe(true);
      expect(projector.canHandle('UserConfirmed')).toBe(true);
      expect(projector.canHandle('UnknownEvent')).toBe(false);
    });
  });
});