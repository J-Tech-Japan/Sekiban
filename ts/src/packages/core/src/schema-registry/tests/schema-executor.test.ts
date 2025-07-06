import { describe, it, expect, beforeEach } from 'vitest';
import { z } from 'zod';
import { ok, err } from 'neverthrow';
import { defineEvent, defineCommand, defineProjector } from '../index.js';
import { SchemaRegistry } from '../registry.js';
import { SchemaExecutor } from '../schema-executor.js';
import { InMemoryEventStore } from '../../events/in-memory-event-store.js';
import { PartitionKeys } from '../../documents/partition-keys.js';
import { ValidationError } from '../../result/errors.js';

// Define test domain
const UserCreatedEvent = defineEvent({
  type: 'UserCreated',
  schema: z.object({
    userId: z.string(),
    name: z.string(),
    email: z.string().email()
  })
});

const UserUpdatedEvent = defineEvent({
  type: 'UserUpdated',
  schema: z.object({
    userId: z.string(),
    name: z.string().optional(),
    email: z.string().email().optional()
  })
});

const UserProjector = defineProjector({
  aggregateType: 'User',
  initialState: () => ({ aggregateType: 'Empty' as const }),
  projections: {
    UserCreated: (state, event) => ({
      aggregateType: 'User' as const,
      userId: event.userId,
      name: event.name,
      email: event.email,
      version: 1
    }),
    UserUpdated: (state, event) => ({
      ...state,
      aggregateType: 'User' as const,
      name: event.name ?? (state as any).name,
      email: event.email ?? (state as any).email,
      version: ((state as any).version || 0) + 1
    })
  }
});

const CreateUserCommand = defineCommand({
  type: 'CreateUser',
  schema: z.object({
    name: z.string().min(1),
    email: z.string().email()
  }),
  projector: UserProjector,
  handlers: {
    specifyPartitionKeys: () => PartitionKeys.generate('User'),
    validate: (data) => {
      // Additional business validation
      if (data.name.toLowerCase().includes('admin')) {
        return err(new ValidationError('Name cannot contain "admin"'));
      }
      return ok(undefined);
    },
    handle: (data, _context) => {
      const userId = crypto.randomUUID();
      const event = UserCreatedEvent.create({
        userId,
        name: data.name,
        email: data.email
      });
      return ok([event]);
    }
  }
});

const UpdateUserCommand = defineCommand({
  type: 'UpdateUser',
  schema: z.object({
    userId: z.string(),
    name: z.string().optional(),
    email: z.string().email().optional()
  }),
  projector: UserProjector,
  handlers: {
    specifyPartitionKeys: (data) => PartitionKeys.existing(data.userId, 'User'),
    validate: () => ok(undefined),
    handle: (data, _context) => {
      const event = UserUpdatedEvent.create({
        userId: data.userId,
        name: data.name,
        email: data.email
      });
      return ok([event]);
    }
  }
});

describe('SchemaExecutor', () => {
  let registry: SchemaRegistry;
  let eventStore: InMemoryEventStore;
  let executor: SchemaExecutor;

  beforeEach(() => {
    registry = new SchemaRegistry();
    eventStore = new InMemoryEventStore();
    
    // Register domain types
    registry.registerEvent(UserCreatedEvent);
    registry.registerEvent(UserUpdatedEvent);
    registry.registerCommand(CreateUserCommand);
    registry.registerCommand(UpdateUserCommand);
    registry.registerProjector(UserProjector);
    
    executor = new SchemaExecutor({ registry, eventStore });
  });

  it('executes create command successfully', async () => {
    // Arrange
    const commandData = {
      name: 'John Doe',
      email: 'john@example.com'
    };

    // Act
    const result = await executor.executeCommand(CreateUserCommand, commandData);

    // Assert
    if (result.isErr()) {
      console.error('Command execution failed:', result.error);
    }
    expect(result.isOk()).toBe(true);
    if (result.isOk()) {
      expect(result.value.success).toBe(true);
      expect(result.value.version).toBe(1);
      expect(result.value.eventIds).toHaveLength(1);
    }
  });

  it('validates command data', async () => {
    // Arrange
    const invalidData = {
      name: 'Admin User',
      email: 'admin@example.com'
    };

    // Act
    const result = await executor.executeCommand(CreateUserCommand, invalidData);

    // Assert
    expect(result.isErr()).toBe(true);
    if (result.isErr()) {
      expect(result.error.message).toContain('admin');
    }
  });

  it('executes update command with existing aggregate', async () => {
    // Arrange - Create user first
    const createData = {
      name: 'John Doe',
      email: 'john@example.com'
    };
    const createResult = await executor.executeCommand(CreateUserCommand, createData);
    expect(createResult.isOk()).toBe(true);
    
    const aggregateId = createResult._unsafeUnwrap().aggregateId;
    
    // Act - Update user
    const updateData = {
      userId: aggregateId,
      name: 'John Smith'
    };
    const updateResult = await executor.executeCommand(UpdateUserCommand, updateData);

    // Assert
    expect(updateResult.isOk()).toBe(true);
    if (updateResult.isOk()) {
      expect(updateResult.value.success).toBe(true);
      expect(updateResult.value.version).toBe(2);
      expect(updateResult.value.eventIds).toHaveLength(1);
    }
  });

  it('queries aggregate state', async () => {
    // Arrange - Create and update user
    const createData = {
      name: 'John Doe',
      email: 'john@example.com'
    };
    const createResult = await executor.executeCommand(CreateUserCommand, createData);
    expect(createResult.isOk()).toBe(true);
    
    const aggregateId = createResult._unsafeUnwrap().aggregateId;
    
    const updateData = {
      userId: aggregateId,
      name: 'John Smith'
    };
    await executor.executeCommand(UpdateUserCommand, updateData);

    // Act - Query aggregate
    const partitionKeys = PartitionKeys.existing(aggregateId, 'User');
    const queryResult = await executor.queryAggregate(partitionKeys, UserProjector);

    // Assert
    expect(queryResult.isOk()).toBe(true);
    if (queryResult.isOk()) {
      const response = queryResult.value;
      expect(response.version).toBe(2);
      expect(response.data.version).toBe(2);
      expect(response.data.payload.aggregateType).toBe('User');
      expect((response.data.payload as any).name).toBe('John Smith');
      expect((response.data.payload as any).email).toBe('john@example.com');
      expect(response.lastEventId).toBeDefined();
    }
  });

  it('handles invalid command schema', async () => {
    // Arrange - Invalid email format
    const invalidData = {
      name: 'John Doe',
      email: 'not-an-email'
    };

    // Act
    const result = await executor.executeCommand(CreateUserCommand, invalidData);

    // Assert
    expect(result.isErr()).toBe(true);
    if (result.isErr()) {
      expect(result.error.message).toContain('email');
    }
  });
});