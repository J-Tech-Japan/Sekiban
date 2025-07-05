import { describe, it, expect, beforeEach } from 'vitest';
import { z } from 'zod';
import { SchemaRegistry } from '../registry.js';
import { defineEvent } from '../event-schema.js';
import { defineCommand } from '../command-schema.js';
import { defineProjector } from '../projector-schema.js';
import { PartitionKeys } from '../../documents/partition-keys.js';
import { ok } from 'neverthrow';
import type { ITypedAggregatePayload } from '../../aggregates/aggregate-projector.js';

// Test types
interface UserPayload extends ITypedAggregatePayload {
  readonly aggregateType: 'User';
  readonly userId: string;
  readonly name: string;
  readonly email: string;
}

type UserPayloadUnion = UserPayload;

// Test event
const UserCreated = defineEvent({
  type: 'UserCreated',
  schema: z.object({
    userId: z.string(),
    name: z.string(),
    email: z.string().email()
  })
});

// Test command
const CreateUser = defineCommand({
  type: 'CreateUser',
  schema: z.object({
    name: z.string(),
    email: z.string().email()
  }),
  handlers: {
    specifyPartitionKeys: () => PartitionKeys.generate('User'),
    validate: () => ok(undefined),
    handle: () => ok([])
  }
});

// Test projector
const userProjector = defineProjector<UserPayloadUnion>({
  aggregateType: 'User',
  initialState: () => ({ aggregateType: 'Empty' as const }),
  projections: {
    UserCreated: (state, event) => ({
      aggregateType: 'User' as const,
      userId: event.userId,
      name: event.name,
      email: event.email
    })
  }
});

describe('SchemaRegistry', () => {
  let registry: SchemaRegistry;

  beforeEach(() => {
    registry = new SchemaRegistry();
  });

  // Test 1: registerEvent stores event schema
  it('registerEvent stores event schema', () => {
    // Act
    const result = registry.registerEvent(UserCreated);

    // Assert
    expect(result).toBe(UserCreated);
    expect(registry.getEventSchema('UserCreated')).toBe(UserCreated.schema);
  });

  // Test 2: registerCommand stores command definition
  it('registerCommand stores command definition', () => {
    // Act
    const result = registry.registerCommand(CreateUser);

    // Assert
    expect(result).toBe(CreateUser);
    expect(registry.getCommand('CreateUser')).toBe(CreateUser);
  });

  // Test 3: registerProjector stores projector definition
  it('registerProjector stores projector definition', () => {
    // Act
    const result = registry.registerProjector(userProjector);

    // Assert
    expect(result).toBe(userProjector);
    expect(registry.getProjector('User')).toBe(userProjector);
  });

  // Test 4: deserializeEvent validates with schema
  it('deserializeEvent validates with schema', () => {
    // Arrange
    registry.registerEvent(UserCreated);
    const validData = {
      userId: 'user-123',
      name: 'John Doe',
      email: 'john@example.com'
    };

    // Act
    const result = registry.deserializeEvent('UserCreated', validData);

    // Assert
    expect(result.type).toBe('UserCreated');
    expect(result.userId).toBe('user-123');
    expect(result.name).toBe('John Doe');
    expect(result.email).toBe('john@example.com');
  });

  // Test 5: deserializeEvent throws for unknown type
  it('deserializeEvent throws for unknown type', () => {
    // Arrange
    const data = { userId: 'test' };

    // Act & Assert
    expect(() => registry.deserializeEvent('UnknownEvent', data))
      .toThrow('Unknown event type: UnknownEvent');
  });

  // Test 6: deserializeEvent throws for invalid data
  it('deserializeEvent throws for invalid data', () => {
    // Arrange
    registry.registerEvent(UserCreated);
    const invalidData = {
      userId: 123, // Should be string
      name: 'John',
      email: 'invalid-email' // Should be valid email
    };

    // Act & Assert
    expect(() => registry.deserializeEvent('UserCreated', invalidData))
      .toThrow();
  });

  // Test 7: getCommand returns registered command
  it('getCommand returns registered command', () => {
    // Arrange
    registry.registerCommand(CreateUser);

    // Act
    const result = registry.getCommand('CreateUser');

    // Assert
    expect(result).toBe(CreateUser);
    expect(result?.type).toBe('CreateUser');
  });

  // Test 8: getCommand returns undefined for unknown command
  it('getCommand returns undefined for unknown command', () => {
    // Act
    const result = registry.getCommand('UnknownCommand');

    // Assert
    expect(result).toBeUndefined();
  });

  // Test 9: getProjector returns registered projector
  it('getProjector returns registered projector', () => {
    // Arrange
    registry.registerProjector(userProjector);

    // Act
    const result = registry.getProjector('User');

    // Assert
    expect(result).toBe(userProjector);
    expect(result?.aggregateType).toBe('User');
  });

  // Test 10: getProjector returns undefined for unknown projector
  it('getProjector returns undefined for unknown projector', () => {
    // Act
    const result = registry.getProjector('UnknownAggregate');

    // Assert
    expect(result).toBeUndefined();
  });

  // Test 11: Registry handles duplicate registrations gracefully
  it('handles duplicate registrations gracefully', () => {
    // Arrange
    const anotherUserCreated = defineEvent({
      type: 'UserCreated',
      schema: z.object({
        userId: z.string(),
        differentField: z.string()
      })
    });

    // Act - Register same type twice
    registry.registerEvent(UserCreated);
    registry.registerEvent(anotherUserCreated); // Should not throw

    // Assert - Latest registration should win
    const registeredSchema = registry.getEventSchema('UserCreated');
    expect(registeredSchema).toBe(anotherUserCreated.schema);
  });

  // Test 12: Registry provides introspection methods
  it('provides introspection methods', () => {
    // Arrange
    registry.registerEvent(UserCreated);
    registry.registerCommand(CreateUser);
    registry.registerProjector(userProjector);

    // Act
    const eventTypes = registry.getEventTypes();
    const commandTypes = registry.getCommandTypes();
    const projectorTypes = registry.getProjectorTypes();

    // Assert
    expect(eventTypes).toContain('UserCreated');
    expect(commandTypes).toContain('CreateUser');
    expect(projectorTypes).toContain('User');
  });

  // Test 13: Registry supports clearing all registrations
  it('supports clearing all registrations', () => {
    // Arrange
    registry.registerEvent(UserCreated);
    registry.registerCommand(CreateUser);
    registry.registerProjector(userProjector);

    // Act
    registry.clear();

    // Assert
    expect(registry.getEventTypes()).toHaveLength(0);
    expect(registry.getCommandTypes()).toHaveLength(0);
    expect(registry.getProjectorTypes()).toHaveLength(0);
    expect(registry.getEventSchema('UserCreated')).toBeUndefined();
    expect(registry.getCommand('CreateUser')).toBeUndefined();
    expect(registry.getProjector('User')).toBeUndefined();
  });

  // Test 14: Registry supports safe parsing
  it('supports safe parsing for events', () => {
    // Arrange
    registry.registerEvent(UserCreated);
    const validData = {
      userId: 'user-123',
      name: 'John Doe',
      email: 'john@example.com'
    };
    const invalidData = {
      userId: 123,
      name: '',
      email: 'invalid'
    };

    // Act
    const validResult = registry.safeDeserializeEvent('UserCreated', validData);
    const invalidResult = registry.safeDeserializeEvent('UserCreated', invalidData);
    const unknownResult = registry.safeDeserializeEvent('UnknownEvent', validData);

    // Assert
    expect(validResult.success).toBe(true);
    if (validResult.success) {
      expect(validResult.data.type).toBe('UserCreated');
    }
    
    expect(invalidResult.success).toBe(false);
    expect(unknownResult.success).toBe(false);
  });

  // Test 15: Registry maintains registration order
  it('maintains registration order', () => {
    // Arrange
    const Event1 = defineEvent({ type: 'Event1', schema: z.object({ id: z.string() }) });
    const Event2 = defineEvent({ type: 'Event2', schema: z.object({ id: z.string() }) });
    const Event3 = defineEvent({ type: 'Event3', schema: z.object({ id: z.string() }) });

    // Act
    registry.registerEvent(Event1);
    registry.registerEvent(Event2);
    registry.registerEvent(Event3);

    // Assert
    const eventTypes = registry.getEventTypes();
    expect(eventTypes).toEqual(['Event1', 'Event2', 'Event3']);
  });

  // Test 16: Registry supports complex nested schemas
  it('supports complex nested schemas', () => {
    // Arrange
    const OrderPlaced = defineEvent({
      type: 'OrderPlaced',
      schema: z.object({
        orderId: z.string().uuid(),
        customer: z.object({
          id: z.string(),
          name: z.string(),
          email: z.string().email()
        }),
        items: z.array(z.object({
          productId: z.string(),
          quantity: z.number().positive(),
          price: z.number().positive()
        })).min(1),
        metadata: z.record(z.any()).optional()
      })
    });

    registry.registerEvent(OrderPlaced);

    const complexData = {
      orderId: '550e8400-e29b-41d4-a716-446655440000',
      customer: {
        id: 'cust123',
        name: 'John Doe',
        email: 'john@example.com'
      },
      items: [
        { productId: 'prod1', quantity: 2, price: 29.99 },
        { productId: 'prod2', quantity: 1, price: 49.99 }
      ],
      metadata: {
        source: 'web',
        campaign: 'summer2024'
      }
    };

    // Act
    const result = registry.deserializeEvent('OrderPlaced', complexData);

    // Assert
    expect(result.type).toBe('OrderPlaced');
    expect(result.customer.name).toBe('John Doe');
    expect(result.items).toHaveLength(2);
    expect(result.metadata?.source).toBe('web');
  });
});