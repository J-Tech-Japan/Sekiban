import { describe, it, expect } from 'vitest';
import { ok, err } from 'neverthrow';
import { v4 as uuidv4 } from 'uuid';
import { CreationCommand, ConstrainedPayloadCommand } from './command.js';
import type { ITypedAggregatePayload, EmptyAggregatePayload } from '../aggregates/aggregate-projector.js';
import type { Aggregate } from '../aggregates/aggregate.js';
import type { IEventPayload } from '../events/event-payload.js';
import type { PartitionKeys } from '../partition-keys/partition-keys.js';
import type { CommandValidationError, SekibanError } from '../errors/sekiban-error.js';

// Test domain payloads
interface UnconfirmedUserPayload extends ITypedAggregatePayload {
  readonly aggregateType: 'UnconfirmedUser';
  id: string;
  name: string;
  email: string;
}

interface ConfirmedUserPayload extends ITypedAggregatePayload {
  readonly aggregateType: 'ConfirmedUser';
  id: string;
  name: string;
  email: string;
  confirmedAt: string;
}

type UserPayloadUnion = UnconfirmedUserPayload | ConfirmedUserPayload;

// Test creation command
class CreateUserCommand extends CreationCommand<UserPayloadUnion> {
  readonly commandType = 'CreateUser';
  
  constructor(
    public readonly name: string,
    public readonly email: string
  ) {
    super();
  }
  
  specifyPartitionKeys(): PartitionKeys {
    return {
      aggregateId: uuidv4(),
      partitionKey: 'User',
      rootPartitionKey: 'default'
    };
  }
  
  validate() {
    if (!this.name || !this.email) {
      return err({
        type: 'CommandValidationError',
        message: 'Name and email are required'
      } as CommandValidationError);
    }
    return ok(undefined);
  }
  
  handleCreation(aggregate: Aggregate<EmptyAggregatePayload>) {
    const event: IEventPayload = {
      eventType: 'UserRegistered',
      userId: aggregate.partitionKeys.aggregateId,
      name: this.name,
      email: this.email
    };
    return ok([event]);
  }
}

// Test constrained command
class ConfirmUserCommand extends ConstrainedPayloadCommand<UserPayloadUnion, UnconfirmedUserPayload> {
  readonly commandType = 'ConfirmUser';
  
  constructor(public readonly userId: string) {
    super();
  }
  
  specifyPartitionKeys(): PartitionKeys {
    return {
      aggregateId: this.userId,
      partitionKey: 'User',
      rootPartitionKey: 'default'
    };
  }
  
  validate() {
    if (!this.userId) {
      return err({
        type: 'CommandValidationError',
        message: 'User ID is required'
      } as CommandValidationError);
    }
    return ok(undefined);
  }
  
  getRequiredPayloadType(): string {
    return 'UnconfirmedUser';
  }
  
  handleTyped(aggregate: Aggregate<UnconfirmedUserPayload>) {
    const event: IEventPayload = {
      eventType: 'UserConfirmed',
      userId: aggregate.payload.id,
      confirmedAt: new Date().toISOString()
    };
    return ok([event]);
  }
}

describe('Multi-Payload Commands', () => {
  describe('CreationCommand', () => {
    it('should execute successfully on empty aggregate', () => {
      // Arrange
      const command = new CreateUserCommand('John Doe', 'john@example.com');
      const partitionKeys = command.specifyPartitionKeys();
      const emptyAggregate: Aggregate<EmptyAggregatePayload> = {
        partitionKeys,
        payload: { aggregateType: 'Empty' },
        version: 0,
        lastEventId: null,
        appliedEvents: []
      };
      
      // Act
      const result = command.handle(emptyAggregate);
      
      // Assert
      expect(result.isOk()).toBe(true);
      if (result.isOk()) {
        const events = result.value;
        expect(events).toHaveLength(1);
        expect(events[0].eventType).toBe('UserRegistered');
        expect((events[0] as any).name).toBe('John Doe');
        expect((events[0] as any).email).toBe('john@example.com');
      }
    });
    
    it('should fail when applied to non-empty aggregate', () => {
      // Arrange
      const command = new CreateUserCommand('John Doe', 'john@example.com');
      const partitionKeys = command.specifyPartitionKeys();
      const nonEmptyAggregate: Aggregate<UnconfirmedUserPayload> = {
        partitionKeys,
        payload: {
          aggregateType: 'UnconfirmedUser',
          id: 'user-123',
          name: 'Existing User',
          email: 'existing@example.com'
        },
        version: 1,
        lastEventId: 'event-1',
        appliedEvents: []
      };
      
      // Act
      const result = command.handle(nonEmptyAggregate as any);
      
      // Assert
      expect(result.isErr()).toBe(true);
      if (result.isErr()) {
        expect(result.error.message).toContain('Creation command can only be applied to empty aggregates');
      }
    });
    
    it('should validate command data', () => {
      // Arrange
      const invalidCommand = new CreateUserCommand('', ''); // Invalid data
      
      // Act
      const result = invalidCommand.validate();
      
      // Assert
      expect(result.isErr()).toBe(true);
      if (result.isErr()) {
        expect(result.error.message).toContain('Name and email are required');
      }
    });
  });
  
  describe('ConstrainedPayloadCommand', () => {
    it('should execute successfully on correct payload type', () => {
      // Arrange
      const command = new ConfirmUserCommand('user-123');
      const partitionKeys = command.specifyPartitionKeys();
      const unconfirmedUserAggregate: Aggregate<UnconfirmedUserPayload> = {
        partitionKeys,
        payload: {
          aggregateType: 'UnconfirmedUser',
          id: 'user-123',
          name: 'John Doe',
          email: 'john@example.com'
        },
        version: 1,
        lastEventId: 'event-1',
        appliedEvents: []
      };
      
      // Act
      const result = command.handle(unconfirmedUserAggregate as any);
      
      // Assert
      expect(result.isOk()).toBe(true);
      if (result.isOk()) {
        const events = result.value;
        expect(events).toHaveLength(1);
        expect(events[0].eventType).toBe('UserConfirmed');
        expect((events[0] as any).userId).toBe('user-123');
      }
    });
    
    it('should fail on incorrect payload type', () => {
      // Arrange
      const command = new ConfirmUserCommand('user-123');
      const partitionKeys = command.specifyPartitionKeys();
      const confirmedUserAggregate: Aggregate<ConfirmedUserPayload> = {
        partitionKeys,
        payload: {
          aggregateType: 'ConfirmedUser',
          id: 'user-123',
          name: 'John Doe',
          email: 'john@example.com',
          confirmedAt: '2025-07-03T10:00:00.000Z'
        },
        version: 2,
        lastEventId: 'event-2',
        appliedEvents: []
      };
      
      // Act
      const result = command.handle(confirmedUserAggregate as any);
      
      // Assert
      expect(result.isErr()).toBe(true);
      if (result.isErr()) {
        expect(result.error.message).toContain("Command requires payload type 'UnconfirmedUser' but found 'ConfirmedUser'");
      }
    });
    
    it('should fail on empty aggregate', () => {
      // Arrange
      const command = new ConfirmUserCommand('user-123');
      const partitionKeys = command.specifyPartitionKeys();
      const emptyAggregate: Aggregate<EmptyAggregatePayload> = {
        partitionKeys,
        payload: { aggregateType: 'Empty' },
        version: 0,
        lastEventId: null,
        appliedEvents: []
      };
      
      // Act
      const result = command.handle(emptyAggregate as any);
      
      // Assert
      expect(result.isErr()).toBe(true);
      if (result.isErr()) {
        expect(result.error.message).toContain("Command requires payload type 'UnconfirmedUser' but found 'Empty'");
      }
    });
    
    it('should provide correct required payload type', () => {
      // Arrange
      const command = new ConfirmUserCommand('user-123');
      
      // Act & Assert
      expect(command.getRequiredPayloadType()).toBe('UnconfirmedUser');
    });
  });
});