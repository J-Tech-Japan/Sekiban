import { describe, it, expect, beforeEach } from 'vitest';
import { ok, err } from 'neverthrow';
import { z } from 'zod';
import { UnifiedCommandExecutor, createUnifiedExecutor } from './unified-executor';
import { defineCommand, defineEvent, defineProjector } from '../schema-registry/index';
import { PartitionKeys } from '../documents/index';
import type { IEventStore } from '../events/store';
import type { IAggregateLoader } from '../aggregates/loader';
import type { IEvent } from '../events/event';
import type { ITypedAggregatePayload } from '../aggregates/aggregate-projector';
import { CommandValidationError } from '../result/errors';

// Test domain setup
interface ActiveTask extends ITypedAggregatePayload {
  aggregateType: 'ActiveTask';
  taskId: string;
  title: string;
  completed: boolean;
}

interface CompletedTask extends ITypedAggregatePayload {
  aggregateType: 'CompletedTask';
  taskId: string;
  title: string;
  completedAt: string;
}

type TaskPayloadUnion = ActiveTask | CompletedTask;

// Events
const TaskCreated = defineEvent({
  type: 'TaskCreated',
  schema: z.object({
    taskId: z.string(),
    title: z.string()
  })
});

const TaskCompleted = defineEvent({
  type: 'TaskCompleted',
  schema: z.object({
    taskId: z.string(),
    completedAt: z.string()
  })
});

// Projector
const TaskProjector = defineProjector<TaskPayloadUnion>({
  aggregateType: 'Task',
  initialState: () => ({ aggregateType: 'Empty' as const }),
  projections: {
    TaskCreated: (state, event) => ({
      aggregateType: 'ActiveTask' as const,
      taskId: event.taskId,
      title: event.title,
      completed: false
    }),
    TaskCompleted: (state, event) => {
      if (state.aggregateType !== 'ActiveTask') return state;
      return {
        aggregateType: 'CompletedTask' as const,
        taskId: state.taskId,
        title: state.title,
        completedAt: event.completedAt
      };
    }
  }
});

// Commands
const CreateTask = defineCommand({
  type: 'CreateTask',
  schema: z.object({
    title: z.string().min(1)
  }),
  projector: TaskProjector,
  handlers: {
    specifyPartitionKeys: () => PartitionKeys.generate('Task'),
    validate: (data) => {
      if (data.title.length > 100) {
        return err(new CommandValidationError('CreateTask', ['Title too long']));
      }
      return ok(undefined);
    },
    handle: (data, context) => {
      const taskId = context.getPartitionKeys().aggregateId;
      return ok([TaskCreated.create({ taskId, title: data.title })]);
    }
  }
});

const CompleteTask = defineCommand({
  type: 'CompleteTask',
  schema: z.object({
    taskId: z.string()
  }),
  projector: TaskProjector,
  requiredPayloadType: 'ActiveTask',
  handlers: {
    specifyPartitionKeys: (data) => PartitionKeys.existing(data.taskId, 'Task'),
    handle: (data, context) => {
      return ok([TaskCompleted.create({
        taskId: data.taskId,
        completedAt: new Date().toISOString()
      })]);
    }
  }
});

// Mock implementations
class MockEventStore implements IEventStore {
  private events: Map<string, IEvent[]> = new Map();
  
  async appendEvents(
    partitionKeys: PartitionKeys,
    events: IEvent[],
    expectedVersion?: number
  ) {
    const key = partitionKeys.toPrimaryKeysString();
    const existing = this.events.get(key) || [];
    this.events.set(key, [...existing, ...events]);
    return ok(undefined);
  }
  
  async getEvents(partitionKeys: PartitionKeys) {
    const key = partitionKeys.toPrimaryKeysString();
    return ok(this.events.get(key) || []);
  }
  
  async getAllEvents() {
    const allEvents: IEvent[] = [];
    for (const events of this.events.values()) {
      allEvents.push(...events);
    }
    return ok(allEvents);
  }
}

class MockAggregateLoader implements IAggregateLoader {
  constructor(private eventStore: MockEventStore) {}
  
  async load(partitionKeys: PartitionKeys, aggregateType: string) {
    const eventsResult = await this.eventStore.getEvents(partitionKeys);
    if (eventsResult.isErr()) return null;
    
    const events = eventsResult.value;
    if (events.length === 0) return null;
    
    return {
      partitionKeys,
      aggregateType,
      version: events.length,
      events
    };
  }
}

describe('UnifiedCommandExecutor', () => {
  let executor: UnifiedCommandExecutor;
  let eventStore: MockEventStore;
  let aggregateLoader: MockAggregateLoader;
  
  beforeEach(() => {
    eventStore = new MockEventStore();
    aggregateLoader = new MockAggregateLoader(eventStore);
    executor = createUnifiedExecutor(eventStore, aggregateLoader);
  });
  
  describe('execute', () => {
    it('should execute a creation command successfully', async () => {
      const command = CreateTask.create({ title: 'Test Task' });
      const result = await executor.execute(command);
      
      expect(result.isOk()).toBe(true);
      if (result.isOk()) {
        expect(result.value.events).toHaveLength(1);
        expect(result.value.events[0].eventType).toBe('TaskCreated');
        expect(result.value.version).toBe(1);
      }
    });
    
    it('should validate commands', async () => {
      const command = CreateTask.create({ title: 'A'.repeat(101) }); // Too long
      const result = await executor.execute(command);
      
      expect(result.isErr()).toBe(true);
      if (result.isErr()) {
        expect(result.error).toBeInstanceOf(CommandValidationError);
        expect((result.error as CommandValidationError).validationErrors).toContain('Title too long');
      }
    });
    
    it('should skip validation when requested', async () => {
      const command = CreateTask.create({ title: 'A'.repeat(101) });
      const result = await executor.execute(command, { skipValidation: true });
      
      expect(result.isOk()).toBe(true);
    });
    
    it('should enforce payload type constraints', async () => {
      // First create a task
      const createCommand = CreateTask.create({ title: 'Test Task' });
      const createResult = await executor.execute(createCommand);
      
      expect(createResult.isOk()).toBe(true);
      const taskId = createResult.isOk() ? createResult.value.aggregateId : '';
      
      // Then complete it
      const completeCommand = CompleteTask.create({ taskId });
      const completeResult = await executor.execute(completeCommand);
      
      expect(completeResult.isOk()).toBe(true);
      if (completeResult.isOk()) {
        expect(completeResult.value.events).toHaveLength(1);
        expect(completeResult.value.events[0].eventType).toBe('TaskCompleted');
        expect(completeResult.value.version).toBe(2);
      }
    });
    
    it('should fail when payload type constraint is not met', async () => {
      // Try to complete a non-existent task
      const command = CompleteTask.create({ taskId: 'non-existent' });
      const result = await executor.execute(command);
      
      expect(result.isErr()).toBe(true);
      if (result.isErr()) {
        expect(result.error.message).toContain('requires payload type');
      }
    });
    
    it('should handle custom metadata', async () => {
      const command = CreateTask.create({ title: 'Test Task' });
      const metadata = {
        userId: 'user-123',
        source: 'test'
      };
      
      const result = await executor.execute(command, { metadata });
      
      expect(result.isOk()).toBe(true);
      if (result.isOk()) {
        expect(result.value.metadata).toMatchObject(metadata);
        expect(result.value.events[0].metadata).toMatchObject(metadata);
      }
    });
    
    it('should use service provider when available', async () => {
      class TestService {
        getValue() { return 'test-value'; }
      }
      
      const serviceProvider = {
        getService: <T>(serviceType: new (...args: any[]) => T) => {
          if (serviceType === TestService) {
            return new TestService() as T;
          }
          return undefined;
        }
      };
      
      const CommandWithService = defineCommand({
        type: 'CommandWithService',
        schema: z.object({ value: z.string() }),
        projector: TaskProjector,
        handlers: {
          specifyPartitionKeys: () => PartitionKeys.generate('Task'),
          handle: (data, context) => {
            const serviceResult = context.getService(TestService);
            expect(serviceResult.isOk()).toBe(true);
            if (serviceResult.isOk()) {
              expect(serviceResult.value.getValue()).toBe('test-value');
            }
            return ok([]);
          }
        }
      });
      
      const command = CommandWithService.create({ value: 'test' });
      const result = await executor.execute(command, { serviceProvider });
      
      expect(result.isOk()).toBe(true);
    });
  });
});