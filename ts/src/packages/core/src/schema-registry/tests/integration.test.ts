import { describe, it, expect, beforeEach } from 'vitest';
import { z } from 'zod';
import { ok, err } from 'neverthrow';
import { 
  defineEvent, 
  defineCommand, 
  defineProjector,
  SchemaRegistry,
  SchemaExecutor,
  globalRegistry
} from '../index';
import { InMemoryEventStore } from '../../events/in-memory-event-store';
import { PartitionKeys } from '../../documents/partition-keys';
import { ValidationError } from '../../result/errors';

describe('Schema Registry Integration Tests', () => {
  let registry: SchemaRegistry;
  let eventStore: InMemoryEventStore;
  let executor: SchemaExecutor;

  beforeEach(() => {
    registry = new SchemaRegistry();
    eventStore = new InMemoryEventStore();
    executor = new SchemaExecutor({ registry, eventStore });
  });

  describe('Event Registration and Deserialization', () => {
    it('registers and deserializes events correctly', () => {
      // Define event
      const AccountOpened = defineEvent({
        type: 'AccountOpened',
        schema: z.object({
          accountId: z.string(),
          accountHolder: z.string(),
          initialBalance: z.number().min(0),
          currency: z.string().length(3)
        })
      });

      // Register event
      registry.registerEvent(AccountOpened);

      // Test event creation
      const event = AccountOpened.create({
        accountId: 'acc-123',
        accountHolder: 'John Doe',
        initialBalance: 1000,
        currency: 'USD'
      });

      expect(event.type).toBe('AccountOpened');
      expect(event.accountId).toBe('acc-123');

      // Test deserialization
      const serialized = JSON.stringify(event);
      const parsed = JSON.parse(serialized);
      const deserialized = registry.deserializeEvent('AccountOpened', parsed);

      expect(deserialized.type).toBe('AccountOpened');
      expect(deserialized.accountId).toBe('acc-123');
      expect(deserialized.accountHolder).toBe('John Doe');
    });

    it('handles invalid event data', () => {
      const SimpleEvent = defineEvent({
        type: 'SimpleEvent',
        schema: z.object({
          id: z.string(),
          amount: z.number()
        })
      });

      registry.registerEvent(SimpleEvent);

      // Try to deserialize invalid data
      const invalidData = { id: 123, amount: 'not a number' };
      
      expect(() => {
        registry.deserializeEvent('SimpleEvent', invalidData);
      }).toThrow();

      // Safe deserialization
      const result = registry.safeDeserializeEvent('SimpleEvent', invalidData);
      expect(result.success).toBe(false);
      if (!result.success) {
        expect(result.error.issues).toHaveLength(2);
      }
    });
  });

  describe('Command Validation Pipeline', () => {
    it('validates commands through multiple layers', () => {
      const CreateProduct = defineCommand({
        type: 'CreateProduct',
        schema: z.object({
          name: z.string().min(3).max(100),
          price: z.number().positive(),
          category: z.enum(['electronics', 'clothing', 'food'])
        }),
        handlers: {
          specifyPartitionKeys: () => PartitionKeys.generate('Product'),
          validate: (data) => {
            // Business rule: electronics must be > $50
            if (data.category === 'electronics' && data.price < 50) {
              return err(new ValidationError('Electronics must be priced at least $50'));
            }
            return ok(undefined);
          },
          handle: (data) => ok([])
        }
      });

      registry.registerCommand(CreateProduct);

      // Test schema validation
      const invalidSchemaResult = CreateProduct.validate({
        name: 'AB', // Too short
        price: -10, // Negative
        category: 'toys' // Invalid enum
      });
      expect(invalidSchemaResult.isErr()).toBe(true);

      // Test business validation
      const invalidBusinessResult = CreateProduct.validate({
        name: 'Cheap Phone',
        price: 30, // Too cheap for electronics
        category: 'electronics'
      });
      expect(invalidBusinessResult.isErr()).toBe(true);
      if (invalidBusinessResult.isErr()) {
        expect(invalidBusinessResult.error.message).toContain('$50');
      }

      // Test valid command
      const validResult = CreateProduct.validate({
        name: 'Gaming Laptop',
        price: 999.99,
        category: 'electronics'
      });
      expect(validResult.isOk()).toBe(true);
    });
  });

  describe('Complex Projector Scenarios', () => {
    it('handles multi-state transitions', async () => {
      // Events
      const TaskCreated = defineEvent({
        type: 'TaskCreated',
        schema: z.object({
          taskId: z.string(),
          title: z.string(),
          assignee: z.string().optional()
        })
      });

      const TaskAssigned = defineEvent({
        type: 'TaskAssigned',
        schema: z.object({
          taskId: z.string(),
          assignee: z.string()
        })
      });

      const TaskCompleted = defineEvent({
        type: 'TaskCompleted',
        schema: z.object({
          taskId: z.string(),
          completedAt: z.string().datetime()
        })
      });

      const TaskArchived = defineEvent({
        type: 'TaskArchived',
        schema: z.object({
          taskId: z.string(),
          reason: z.string()
        })
      });

      // Projector with multiple state types
      const TaskProjector = defineProjector({
        aggregateType: 'Task',
        initialState: () => ({ aggregateType: 'Empty' as const }),
        projections: {
          TaskCreated: (state, event) => ({
            aggregateType: 'PendingTask' as const,
            taskId: event.taskId,
            title: event.title,
            assignee: event.assignee,
            status: 'pending' as const
          }),
          
          TaskAssigned: (state, event) => {
            if (state.aggregateType === 'PendingTask') {
              return {
                ...state,
                assignee: event.assignee,
                status: 'assigned' as const
              };
            }
            return state;
          },
          
          TaskCompleted: (state, event) => {
            if (state.aggregateType === 'PendingTask') {
              return {
                aggregateType: 'CompletedTask' as const,
                taskId: event.taskId,
                completedAt: event.completedAt,
                status: 'completed' as const
              };
            }
            return state;
          },
          
          TaskArchived: (state, event) => ({
            aggregateType: 'ArchivedTask' as const,
            taskId: event.taskId,
            reason: event.reason,
            status: 'archived' as const
          })
        }
      });

      // Register everything
      registry.registerEvent(TaskCreated);
      registry.registerEvent(TaskAssigned);
      registry.registerEvent(TaskCompleted);
      registry.registerEvent(TaskArchived);
      registry.registerProjector(TaskProjector);

      // Create task command
      const CreateTask = defineCommand({
        type: 'CreateTask',
        schema: z.object({
          title: z.string(),
          assignee: z.string().optional()
        }),
        handlers: {
          specifyPartitionKeys: () => PartitionKeys.generate('Task'),
          validate: () => ok(undefined),
          handle: (data, aggregate) => ok([
            TaskCreated.create({
              taskId: aggregate.partitionKeys.aggregateId,
              title: data.title,
              assignee: data.assignee
            })
          ])
        }
      });

      registry.registerCommand(CreateTask);

      // Execute command
      const result = await executor.executeCommand(CreateTask, {
        title: 'Implement feature X'
      });

      expect(result.isOk()).toBe(true);
      const taskId = result._unsafeUnwrap().aggregateId;

      // Query the task
      const queryResult = await executor.queryAggregate(
        PartitionKeys.existing(taskId, 'Task')
      );

      expect(queryResult.isOk()).toBe(true);
      if (queryResult.isOk()) {
        const task = queryResult.value.data.payload as any;
        expect(task.aggregateType).toBe('PendingTask');
        expect(task.status).toBe('pending');
        expect(task.title).toBe('Implement feature X');
      }
    });
  });

  describe('Registry Introspection', () => {
    it('provides complete registry information', () => {
      // Register various types
      const Event1 = defineEvent({
        type: 'Event1',
        schema: z.object({ id: z.string() })
      });

      const Event2 = defineEvent({
        type: 'Event2',
        schema: z.object({ value: z.number() })
      });

      const Command1 = defineCommand({
        type: 'Command1',
        schema: z.object({ data: z.string() }),
        handlers: {
          specifyPartitionKeys: () => PartitionKeys.generate('Test'),
          validate: () => ok(undefined),
          handle: () => ok([])
        }
      });

      const Projector1 = defineProjector({
        aggregateType: 'Type1',
        initialState: () => ({ aggregateType: 'Empty' as const }),
        projections: {}
      });

      registry.registerEvent(Event1);
      registry.registerEvent(Event2);
      registry.registerCommand(Command1);
      registry.registerProjector(Projector1);

      // Test introspection
      expect(registry.getEventTypes()).toContain('Event1');
      expect(registry.getEventTypes()).toContain('Event2');
      expect(registry.getCommandTypes()).toContain('Command1');
      expect(registry.getProjectorTypes()).toContain('Type1');

      // Test counts
      const counts = registry.getRegistrationCounts();
      expect(counts.events).toBe(2);
      expect(counts.commands).toBe(1);
      expect(counts.projectors).toBe(1);

      // Test existence checks
      expect(registry.hasEventType('Event1')).toBe(true);
      expect(registry.hasEventType('NonExistent')).toBe(false);
      expect(registry.hasCommandType('Command1')).toBe(true);
      expect(registry.hasProjectorType('Type1')).toBe(true);
    });
  });

  describe('Global Registry', () => {
    it('uses global registry instance', () => {
      // Clear global registry first
      globalRegistry.clear();

      // Define and register using convenience functions
      const GlobalEvent = defineEvent({
        type: 'GlobalEvent',
        schema: z.object({ message: z.string() })
      });

      // Register in global registry
      globalRegistry.registerEvent(GlobalEvent);

      // Should be accessible
      expect(globalRegistry.hasEventType('GlobalEvent')).toBe(true);
      
      // Test deserialization
      const data = { message: 'Hello from global registry' };
      const deserialized = globalRegistry.deserializeEvent('GlobalEvent', data);
      expect(deserialized.message).toBe('Hello from global registry');
    });
  });

  describe('Error Handling', () => {
    it('handles missing types gracefully', () => {
      // Try to deserialize non-existent event
      const result = registry.safeDeserializeEvent('NonExistentEvent', {});
      expect(result.success).toBe(false);
      if (!result.success) {
        expect(result.error.issues[0].message).toContain('Unknown event type');
      }

      // Try to get non-existent definitions
      expect(registry.getEventSchema('NonExistent')).toBeUndefined();
      expect(registry.getCommand('NonExistent')).toBeUndefined();
      expect(registry.getProjector('NonExistent')).toBeUndefined();
    });
  });

  describe('Schema Evolution', () => {
    it('supports optional fields for backward compatibility', () => {
      // Version 1 of event
      const UserEventV1 = defineEvent({
        type: 'UserEvent',
        schema: z.object({
          userId: z.string(),
          name: z.string()
        })
      });

      registry.registerEvent(UserEventV1);

      // Version 2 adds optional field
      registry.clear();
      const UserEventV2 = defineEvent({
        type: 'UserEvent',
        schema: z.object({
          userId: z.string(),
          name: z.string(),
          email: z.string().email().optional()
        })
      });

      registry.registerEvent(UserEventV2);

      // Old events should still deserialize
      const oldEventData = { userId: '123', name: 'John' };
      const deserialized = registry.deserializeEvent('UserEvent', oldEventData);
      
      expect(deserialized.userId).toBe('123');
      expect(deserialized.name).toBe('John');
      expect(deserialized.email).toBeUndefined();
    });
  });
});