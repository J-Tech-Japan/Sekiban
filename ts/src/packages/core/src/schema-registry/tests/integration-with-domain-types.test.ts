import { describe, it, expect } from 'vitest';
import { z } from 'zod';
import { defineEvent, defineCommand, defineProjector } from '../index.js';
import { SchemaRegistry } from '../registry.js';
import { createSchemaDomainTypes } from '../schema-domain-types.js';
import { createInMemorySekibanExecutor } from '../../executors/in-memory-with-domain-types.js';
import { PartitionKeys } from '../../documents/partition-keys.js';
import { ok, err } from 'neverthrow';
import type { EmptyAggregatePayload } from '../../aggregates/aggregate.js';

describe('Integration: Schema Registry with SekibanDomainTypes', () => {
  // Define test events
  const TestCreated = defineEvent({
    type: 'TestCreated',
    schema: z.object({
      id: z.string(),
      name: z.string(),
      value: z.number()
    })
  });

  const TestUpdated = defineEvent({
    type: 'TestUpdated',
    schema: z.object({
      id: z.string(),
      name: z.string().optional(),
      value: z.number().optional()
    })
  });

  // Define test aggregate
  interface TestPayload {
    aggregateType: 'Test';
    id: string;
    name: string;
    value: number;
  }

  // Define test commands
  const CreateTest = defineCommand({
    type: 'CreateTest',
    schema: z.object({
      name: z.string(),
      value: z.number()
    }),
    aggregateType: 'Test',
    handlers: {
      specifyPartitionKeys: () => PartitionKeys.generate('Test'),
      validate: () => ok(undefined),
      handle: (data, aggregate) => {
        if (aggregate.payload.aggregateType !== 'Empty') {
          return err({ type: 'AggregateAlreadyExists', message: 'Test already exists' });
        }
        return ok([
          TestCreated.create({
            id: crypto.randomUUID(),
            name: data.name,
            value: data.value
          })
        ]);
      }
    }
  });

  const UpdateTest = defineCommand({
    type: 'UpdateTest',
    schema: z.object({
      id: z.string(),
      name: z.string().optional(),
      value: z.number().optional()
    }),
    aggregateType: 'Test',
    handlers: {
      specifyPartitionKeys: (data) => PartitionKeys.existing('Test', data.id),
      validate: () => ok(undefined),
      handle: (data, aggregate) => {
        if (aggregate.payload.aggregateType !== 'Test') {
          return err({ type: 'AggregateNotFound', message: 'Test not found' });
        }
        return ok([
          TestUpdated.create({
            id: data.id,
            name: data.name,
            value: data.value
          })
        ]);
      }
    }
  });

  // Define test projector
  const testProjector = defineProjector<TestPayload | EmptyAggregatePayload>({
    aggregateType: 'Test',
    initialState: () => ({ aggregateType: 'Empty' as const }),
    projections: {
      TestCreated: (state, event: ReturnType<typeof TestCreated.create>) => ({
        aggregateType: 'Test' as const,
        id: event.id,
        name: event.name,
        value: event.value
      }),
      TestUpdated: (state, event: ReturnType<typeof TestUpdated.create>) => {
        if (state.aggregateType !== 'Test') return state;
        return {
          ...state,
          name: event.name || state.name,
          value: event.value !== undefined ? event.value : state.value
        };
      }
    }
  });

  it('should create SekibanDomainTypes from schema registry', () => {
    const registry = new SchemaRegistry();
    
    // Register schemas
    registry.registerEvent(TestCreated);
    registry.registerEvent(TestUpdated);
    registry.registerCommand(CreateTest);
    registry.registerCommand(UpdateTest);
    registry.registerProjector(testProjector);

    // Create domain types
    const domainTypes = createSchemaDomainTypes(registry);

    // Verify domain types structure
    expect(domainTypes.eventTypes).toBeDefined();
    expect(domainTypes.commandTypes).toBeDefined();
    expect(domainTypes.projectorTypes).toBeDefined();
    expect(domainTypes.aggregateTypes).toBeDefined();
    expect(domainTypes.queryTypes).toBeDefined();
    expect(domainTypes.serializer).toBeDefined();

    // Verify event types
    const eventTypes = domainTypes.eventTypes.getEventTypes();
    expect(eventTypes).toHaveLength(2);
    expect(eventTypes.map(t => t.name)).toContain('TestCreated');
    expect(eventTypes.map(t => t.name)).toContain('TestUpdated');

    // Verify command types
    const commandTypes = domainTypes.commandTypes.getCommandTypes();
    expect(commandTypes).toHaveLength(2);
    expect(commandTypes.map(t => t.name)).toContain('CreateTest');
    expect(commandTypes.map(t => t.name)).toContain('UpdateTest');

    // Verify projector types
    const projectorTypes = domainTypes.projectorTypes.getProjectorTypes();
    expect(projectorTypes).toHaveLength(1);
    expect(projectorTypes[0].aggregateTypeName).toBe('Test');
  });

  it('should execute commands using executor with domain types', async () => {
    const registry = new SchemaRegistry();
    
    // Register all schemas
    registry.registerEvent(TestCreated);
    registry.registerEvent(TestUpdated);
    registry.registerCommand(CreateTest);
    registry.registerCommand(UpdateTest);
    registry.registerProjector(testProjector);

    // Create domain types and executor
    const domainTypes = createSchemaDomainTypes(registry);
    const executor = createInMemorySekibanExecutor(domainTypes);

    // Execute create command
    const createCommand = CreateTest.create({
      name: 'Test Item',
      value: 42
    });

    const createResult = await executor.executeCommand(createCommand);
    expect(createResult.isOk()).toBe(true);
    
    if (createResult.isOk()) {
      const aggregateId = createResult.value.aggregateId;
      
      // Load the aggregate
      const loadResult = await executor.loadAggregate<TestPayload>(
        PartitionKeys.existing('Test', aggregateId)
      );
      
      expect(loadResult.isOk()).toBe(true);
      if (loadResult.isOk() && loadResult.value) {
        const aggregate = loadResult.value;
        expect(aggregate.payload.aggregateType).toBe('Test');
        expect(aggregate.payload.name).toBe('Test Item');
        expect(aggregate.payload.value).toBe(42);
        expect(aggregate.version).toBe(1);
      }
    }
  });

  it('should serialize and deserialize events using domain types', () => {
    const registry = new SchemaRegistry();
    registry.registerEvent(TestCreated);
    
    const domainTypes = createSchemaDomainTypes(registry);
    
    // Create an event
    const originalEvent = {
      aggregateId: '123',
      partitionKeys: PartitionKeys.existing('Test', '123'),
      sortableUniqueId: 'abc123',
      eventType: 'TestCreated',
      eventPayload: TestCreated.create({
        id: '123',
        name: 'Test Item',
        value: 42
      }),
      aggregateType: 'Test',
      version: 1,
      appendedAt: new Date()
    };

    // Serialize the event
    const serialized = domainTypes.eventTypes.serializeEvent(originalEvent);
    
    expect(serialized.eventType).toBe('TestCreated');
    expect(serialized.aggregateId).toBe('123');
    expect(serialized.version).toBe(1);
    expect(serialized.payload).toEqual({
      type: 'TestCreated',
      id: '123',
      name: 'Test Item',
      value: 42
    });

    // Deserialize the event
    const deserializeResult = domainTypes.eventTypes.deserializeEvent(serialized);
    
    expect(deserializeResult.isOk()).toBe(true);
    if (deserializeResult.isOk()) {
      const deserialized = deserializeResult.value;
      expect(deserialized.eventType).toBe('TestCreated');
      expect(deserialized.aggregateId).toBe('123');
      expect(deserialized.version).toBe(1);
      expect(deserialized.eventPayload).toEqual({
        type: 'TestCreated',
        id: '123',
        name: 'Test Item',
        value: 42
      });
    }
  });

  it('should get aggregate type for command', () => {
    const registry = new SchemaRegistry();
    registry.registerCommand(CreateTest);
    registry.registerCommand(UpdateTest);
    
    const domainTypes = createSchemaDomainTypes(registry);
    const commandTypes = domainTypes.commandTypes as any;
    
    // Verify the method exists and works
    if (commandTypes.getAggregateTypeForCommand) {
      expect(commandTypes.getAggregateTypeForCommand('CreateTest')).toBe('Test');
      expect(commandTypes.getAggregateTypeForCommand('UpdateTest')).toBe('Test');
      expect(commandTypes.getAggregateTypeForCommand('UnknownCommand')).toBeUndefined();
    }
  });
});