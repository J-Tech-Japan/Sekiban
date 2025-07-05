import { describe, it, expect, vi } from 'vitest';
import { z } from 'zod';
import { ok, err } from 'neverthrow';
import { defineCommand } from '../command-schema.js';
import { defineEvent } from '../event-schema.js';
import { PartitionKeys } from '../../documents/partition-keys.js';
import { Aggregate } from '../../aggregates/aggregate.js';
import { EmptyAggregatePayload } from '../../aggregates/aggregate.js';
import { CommandValidationError } from '../../result/errors.js';
import { ValidationError } from '../../result/errors.js';
import type { ITypedAggregatePayload } from '../../aggregates/aggregate-projector.js';

// Test aggregate payload types
interface UserPayload extends ITypedAggregatePayload {
  readonly aggregateType: 'User';
  readonly userId: string;
  readonly name: string;
  readonly email: string;
}

interface DeletedUserPayload extends ITypedAggregatePayload {
  readonly aggregateType: 'DeletedUser';
}

type UserPayloadUnion = UserPayload | DeletedUserPayload;

// Test event
const UserCreated = defineEvent({
  type: 'UserCreated',
  schema: z.object({
    userId: z.string(),
    name: z.string(),
    email: z.string().email()
  })
});

describe('defineCommand', () => {
  // Test 1: Basic command definition
  it('creates command definition with type property', () => {
    // Arrange
    const definition = {
      type: 'CreateUser' as const,
      schema: z.object({ name: z.string() }),
      handlers: {
        specifyPartitionKeys: () => PartitionKeys.generate('User'),
        validate: () => ok(undefined),
        handle: () => ok([])
      }
    };

    // Act
    const result = defineCommand<'CreateUser', typeof definition.schema, UserPayloadUnion>(definition);

    // Assert
    expect(result.type).toBe('CreateUser');
  });

  // Test 2: Schema validation for correct data
  it('schema validates correct data', () => {
    // Arrange
    const CreateUser = defineCommand({
      type: 'CreateUser',
      schema: z.object({
        name: z.string().min(1),
        email: z.string().email()
      }),
      handlers: {
        specifyPartitionKeys: () => PartitionKeys.generate('User'),
        validate: () => ok(undefined),
        handle: () => ok([])
      }
    });
    const validData = { name: 'John Doe', email: 'john@example.com' };

    // Act
    const result = CreateUser.schema.safeParse(validData);

    // Assert
    expect(result.success).toBe(true);
  });

  // Test 3: specifyPartitionKeys handler
  it('handlers.specifyPartitionKeys returns correct keys', () => {
    // Arrange
    const CreateUser = defineCommand({
      type: 'CreateUser',
      schema: z.object({ name: z.string() }),
      handlers: {
        specifyPartitionKeys: () => PartitionKeys.generate('User', 'default'),
        validate: () => ok(undefined),
        handle: () => ok([])
      }
    });
    const data = { name: 'John' };

    // Act
    const partitionKeys = CreateUser.handlers.specifyPartitionKeys(data);

    // Assert
    expect(partitionKeys.group).toBe('User');
    expect(partitionKeys.rootPartitionKey).toBe('default');
  });

  // Test 4: Business validation
  it('handlers.validate performs business validation', () => {
    // Arrange
    const CreateUser = defineCommand({
      type: 'CreateUser',
      schema: z.object({ email: z.string().email() }),
      handlers: {
        specifyPartitionKeys: () => PartitionKeys.generate('User'),
        validate: (data) => {
          if (data.email.endsWith('@test.com')) {
            return err(new CommandValidationError('CreateUser', ['Test emails not allowed']));
          }
          return ok(undefined);
        },
        handle: () => ok([])
      }
    });

    // Act
    const validResult = CreateUser.handlers.validate({ email: 'john@example.com' });
    const invalidResult = CreateUser.handlers.validate({ email: 'john@test.com' });

    // Assert
    expect(validResult.isOk()).toBe(true);
    expect(invalidResult.isErr()).toBe(true);
    if (invalidResult.isErr()) {
      expect(invalidResult.error).toBeInstanceOf(CommandValidationError);
    }
  });

  // Test 5: Handle returns events
  it('handlers.handle returns events for valid aggregate', () => {
    // Arrange
    const CreateUser = defineCommand({
      type: 'CreateUser',
      schema: z.object({
        name: z.string(),
        email: z.string().email()
      }),
      handlers: {
        specifyPartitionKeys: () => PartitionKeys.generate('User'),
        validate: () => ok(undefined),
        handle: (data, aggregate) => {
          if (aggregate.payload.aggregateType !== 'Empty') {
            return err(new ValidationError('User already exists'));
          }
          return ok([UserCreated.create({
            userId: '123',
            name: data.name,
            email: data.email
          })]);
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

    // Act
    const result = CreateUser.handlers.handle(
      { name: 'John', email: 'john@example.com' },
      emptyAggregate
    );

    // Assert
    expect(result.isOk()).toBe(true);
    if (result.isOk()) {
      expect(result.value).toHaveLength(1);
      expect(result.value[0]).toHaveProperty('type', 'UserCreated');
    }
  });

  // Test 6: Create function adds commandType
  it('create function adds commandType property', () => {
    // Arrange
    const CreateUser = defineCommand({
      type: 'CreateUser',
      schema: z.object({ name: z.string() }),
      handlers: {
        specifyPartitionKeys: () => PartitionKeys.generate('User'),
        validate: () => ok(undefined),
        handle: () => ok([])
      }
    });
    const data = { name: 'John' };

    // Act
    const command = CreateUser.create(data);

    // Assert
    expect(command.commandType).toBe('CreateUser');
    expect(command.name).toBe('John');
  });

  // Test 7: Validate combines schema and business validation
  it('validate combines schema and business validation', () => {
    // Arrange
    const CreateUser = defineCommand({
      type: 'CreateUser',
      schema: z.object({
        name: z.string().min(1),
        email: z.string().email()
      }),
      handlers: {
        specifyPartitionKeys: () => PartitionKeys.generate('User'),
        validate: (data) => {
          if (data.email.endsWith('@blocked.com')) {
            return err(new CommandValidationError('CreateUser', ['Blocked domain']));
          }
          return ok(undefined);
        },
        handle: () => ok([])
      }
    });

    // Act - Schema validation failure
    const schemaFailure = CreateUser.validate({ name: '', email: 'invalid-email' });
    
    // Act - Business validation failure
    const businessFailure = CreateUser.validate({ name: 'John', email: 'john@blocked.com' });
    
    // Act - All validations pass
    const success = CreateUser.validate({ name: 'John', email: 'john@example.com' });

    // Assert
    expect(schemaFailure.isErr()).toBe(true);
    expect(businessFailure.isErr()).toBe(true);
    expect(success.isOk()).toBe(true);
  });

  // Test 8: Execute calls handler with typed data
  it('execute calls handler with typed data', () => {
    // Arrange
    const handleMock = vi.fn((data, aggregate) => ok([
      UserCreated.create({
        userId: '123',
        name: data.name,
        email: data.email
      })
    ]));

    const CreateUser = defineCommand({
      type: 'CreateUser',
      schema: z.object({
        name: z.string(),
        email: z.string().email()
      }),
      handlers: {
        specifyPartitionKeys: () => PartitionKeys.generate('User'),
        validate: () => ok(undefined),
        handle: handleMock
      }
    });

    const aggregate = new Aggregate<UserPayloadUnion | EmptyAggregatePayload>(
      PartitionKeys.generate('User'),
      'User',
      0,
      new EmptyAggregatePayload(),
      null,
      'User',
      1
    );

    const data = { name: 'John', email: 'john@example.com' };

    // Act
    const result = CreateUser.execute(data, aggregate);

    // Assert
    expect(handleMock).toHaveBeenCalledWith(data, aggregate);
    expect(result.isOk()).toBe(true);
    if (result.isOk()) {
      expect(result.value).toHaveLength(1);
    }
  });

  // Test 9: Complex command with nested schema
  it('handles complex command with nested schema', () => {
    // Arrange
    const CreateOrder = defineCommand({
      type: 'CreateOrder',
      schema: z.object({
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
        shippingAddress: z.object({
          street: z.string(),
          city: z.string(),
          zipCode: z.string()
        })
      }),
      handlers: {
        specifyPartitionKeys: (data) => PartitionKeys.generate('Order'),
        validate: (data) => {
          const total = data.items.reduce((sum, item) => sum + item.price * item.quantity, 0);
          if (total > 10000) {
            return err(new CommandValidationError('CreateOrder', ['Order total exceeds limit']));
          }
          return ok(undefined);
        },
        handle: () => ok([])
      }
    });

    const validData = {
      customer: {
        id: 'cust123',
        name: 'John Doe',
        email: 'john@example.com'
      },
      items: [
        { productId: 'prod1', quantity: 2, price: 50 }
      ],
      shippingAddress: {
        street: '123 Main St',
        city: 'Boston',
        zipCode: '02101'
      }
    };

    // Act
    const command = CreateOrder.create(validData);
    const validation = CreateOrder.validate(validData);

    // Assert
    expect(command.commandType).toBe('CreateOrder');
    expect(command.customer.name).toBe('John Doe');
    expect(validation.isOk()).toBe(true);
  });

  // Test 10: Type inference works correctly
  it('provides correct TypeScript types through inference', () => {
    // Arrange
    const UpdateUser = defineCommand({
      type: 'UpdateUser',
      schema: z.object({
        userId: z.string(),
        name: z.string().optional(),
        email: z.string().email().optional()
      }),
      handlers: {
        specifyPartitionKeys: (data) => PartitionKeys.existing(data.userId, 'User'),
        validate: () => ok(undefined),
        handle: () => ok([])
      }
    });

    // Act
    const command = UpdateUser.create({
      userId: '123',
      name: 'Updated Name'
    });

    // Assert - TypeScript compile-time check
    const commandType: 'UpdateUser' = command.commandType;
    const userId: string = command.userId;
    const name: string | undefined = command.name;
    const email: string | undefined = command.email;

    expect(commandType).toBe('UpdateUser');
    expect(userId).toBe('123');
    expect(name).toBe('Updated Name');
    expect(email).toBeUndefined();
  });

  // Test 11: Error handling in handlers
  it('handles errors in command handlers gracefully', () => {
    // Arrange
    const CreateUser = defineCommand({
      type: 'CreateUser',
      schema: z.object({ name: z.string() }),
      handlers: {
        specifyPartitionKeys: () => PartitionKeys.generate('User'),
        validate: () => ok(undefined),
        handle: (data, aggregate) => {
          if (aggregate.payload.aggregateType !== 'Empty') {
            return err(new ValidationError('Aggregate must be empty'));
          }
          return ok([]);
        }
      }
    });

    const nonEmptyAggregate = new Aggregate<UserPayload>(
      PartitionKeys.existing('123', 'User'),
      'User',
      1,
      {
        aggregateType: 'User',
        userId: '123',
        name: 'Existing User',
        email: 'existing@example.com'
      },
      null,
      'User',
      1
    );

    // Act
    const result = CreateUser.execute({ name: 'John' }, nonEmptyAggregate);

    // Assert
    expect(result.isErr()).toBe(true);
    if (result.isErr()) {
      expect(result.error).toBeInstanceOf(ValidationError);
      expect(result.error.message).toBe('Aggregate must be empty');
    }
  });
});