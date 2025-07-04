import { describe, it, expect, beforeEach, vi, MockedFunction } from 'vitest';
import { ok, err, Result } from 'neverthrow';
import type { DaprClient } from '@dapr/dapr';
import type { 
  ICommand,
  IAggregateProjector,
  ITypedAggregatePayload,
  EmptyAggregatePayload,
  PartitionKeys,
  Aggregate
} from '@sekiban/core';
import { SekibanDaprExecutor, type DaprSekibanConfiguration } from './sekiban-dapr-executor.js';

// Mock types for testing
interface TestUserPayload extends ITypedAggregatePayload {
  readonly aggregateType: 'TestUser';
  id: string;
  name: string;
  email: string;
}

interface TestCreateUserCommand extends ICommand<TestUserPayload> {
  readonly commandType: 'CreateTestUser';
  name: string;
  email: string;
}

class MockUserProjector implements IAggregateProjector<TestUserPayload> {
  readonly aggregateTypeName = 'TestUser';
  
  getInitialState(): Aggregate<EmptyAggregatePayload> {
    throw new Error('Not implemented in mock');
  }
  
  project(): Result<Aggregate<TestUserPayload | EmptyAggregatePayload>, any> {
    throw new Error('Not implemented in mock');
  }
  
  canHandle(): boolean {
    return true;
  }
  
  getSupportedPayloadTypes(): string[] {
    return ['TestUser'];
  }
}

class MockOrderProjector implements IAggregateProjector<any> {
  readonly aggregateTypeName = 'TestOrder';
  
  getInitialState(): Aggregate<EmptyAggregatePayload> {
    throw new Error('Not implemented in mock');
  }
  
  project(): Result<Aggregate<any>, any> {
    throw new Error('Not implemented in mock');
  }
  
  canHandle(): boolean {
    return true;
  }
  
  getSupportedPayloadTypes(): string[] {
    return ['TestOrder'];
  }
}

describe('SekibanDaprExecutor', () => {
  let daprClient: MockedFunction<DaprClient>;
  let configuration: DaprSekibanConfiguration;
  let executor: SekibanDaprExecutor;
  let mockProjector: MockUserProjector;

  beforeEach(() => {
    // Mock Dapr client
    daprClient = {
      actors: {
        getActor: vi.fn(),
        invoke: vi.fn()
      },
      pubsub: {
        publish: vi.fn()
      }
    } as any;

    // Configuration matching C# implementation
    configuration = {
      stateStoreName: 'sekiban-eventstore',
      pubSubName: 'sekiban-pubsub', 
      eventTopicName: 'domain-events',
      actorType: 'AggregateActor',
      projectors: []
    };

    mockProjector = new MockUserProjector();
    configuration.projectors = [mockProjector];

    executor = new SekibanDaprExecutor(daprClient, configuration);
  });

  describe('Construction', () => {
    it('should create executor with Dapr client and configuration', () => {
      // Act & Assert
      expect(executor).toBeDefined();
      expect(executor.getDaprClient()).toBe(daprClient);
      expect(executor.getConfiguration()).toEqual(configuration);
    });

    it('should throw error when no projectors provided', () => {
      // Arrange
      const emptyConfig = { ...configuration, projectors: [] };

      // Act & Assert
      expect(() => new SekibanDaprExecutor(daprClient, emptyConfig))
        .toThrow('At least one projector must be provided');
    });

    it('should register all provided projectors', () => {
      // Arrange
      const orderProjector = new MockOrderProjector();
      const configWithMultipleProjectors = {
        ...configuration,
        projectors: [mockProjector, orderProjector]
      };

      // Act
      const executorWithMultiple = new SekibanDaprExecutor(daprClient, configWithMultipleProjectors);

      // Assert
      expect(executorWithMultiple.getRegisteredProjectors()).toHaveLength(2);
      expect(executorWithMultiple.hasProjector('TestUser')).toBe(true);
      expect(executorWithMultiple.hasProjector('TestOrder')).toBe(true);
    });
  });

  describe('Command Execution', () => {
    it('should execute command through Dapr AggregateActor', async () => {
      // Arrange
      const mockCommand: TestCreateUserCommand = {
        commandType: 'CreateTestUser',
        name: 'John Doe',
        email: 'john@example.com',
        specifyPartitionKeys: () => ({
          aggregateId: 'user-123',
          partitionKey: 'TestUser',
          rootPartitionKey: 'default'
        }),
        validate: () => ok(undefined),
        handle: () => ok([])
      };

      const expectedResponse = {
        aggregateId: 'user-123',
        lastSortableUniqueId: '20250703T160000Z.123456.user-123',
        success: true
      };

      // Mock actor response
      const mockActorProxy = {
        executeCommandAsync: vi.fn().mockResolvedValue(expectedResponse)
      };
      
      daprClient.actors.getActor = vi.fn().mockReturnValue(mockActorProxy);

      // Act
      const result = await executor.executeCommandAsync(mockCommand);

      // Assert
      expect(result.isOk()).toBe(true);
      if (result.isOk()) {
        expect(result.value).toEqual(expectedResponse);
      }

      // Verify Dapr actor was called correctly
      expect(daprClient.actors.getActor).toHaveBeenCalledWith(
        'AggregateActor',
        expect.stringContaining('user-123')
      );
      expect(mockActorProxy.executeCommandAsync).toHaveBeenCalledWith(
        expect.objectContaining({
          command: mockCommand,
          partitionKeys: mockCommand.specifyPartitionKeys()
        })
      );
    });

    it('should return error when command validation fails', async () => {
      // Arrange
      const invalidCommand: TestCreateUserCommand = {
        commandType: 'CreateTestUser',
        name: '',
        email: 'invalid-email',
        specifyPartitionKeys: () => ({
          aggregateId: 'user-123',
          partitionKey: 'TestUser',
          rootPartitionKey: 'default'
        }),
        validate: () => err({
          type: 'CommandValidationError',
          message: 'Invalid command data'
        }),
        handle: () => ok([])
      };

      // Act
      const result = await executor.executeCommandAsync(invalidCommand);

      // Assert
      expect(result.isErr()).toBe(true);
      if (result.isErr()) {
        expect(result.error.message).toContain('Invalid command data');
      }

      // Verify no actor call was made
      expect(daprClient.actors.getActor).not.toHaveBeenCalled();
    });

    it('should handle Dapr actor communication errors', async () => {
      // Arrange
      const mockCommand: TestCreateUserCommand = {
        commandType: 'CreateTestUser',
        name: 'John Doe',
        email: 'john@example.com',
        specifyPartitionKeys: () => ({
          aggregateId: 'user-123',
          partitionKey: 'TestUser',
          rootPartitionKey: 'default'
        }),
        validate: () => ok(undefined),
        handle: () => ok([])
      };

      // Mock actor communication failure
      const mockActorProxy = {
        executeCommandAsync: vi.fn().mockRejectedValue(new Error('Dapr actor unavailable'))
      };
      
      daprClient.actors.getActor = vi.fn().mockReturnValue(mockActorProxy);

      // Act
      const result = await executor.executeCommandAsync(mockCommand);

      // Assert
      expect(result.isErr()).toBe(true);
      if (result.isErr()) {
        expect(result.error.message).toContain('Dapr actor unavailable');
      }
    });
  });

  describe('Query Execution', () => {
    it('should execute single-item query through Dapr actor', async () => {
      // Arrange
      const mockQuery = {
        queryType: 'GetTestUser',
        userId: 'user-123'
      };

      const expectedUser: TestUserPayload = {
        aggregateType: 'TestUser',
        id: 'user-123',
        name: 'John Doe',
        email: 'john@example.com'
      };

      // Mock actor response
      const mockActorProxy = {
        queryAsync: vi.fn().mockResolvedValue(expectedUser)
      };
      
      daprClient.actors.getActor = vi.fn().mockReturnValue(mockActorProxy);

      // Act
      const result = await executor.queryAsync(mockQuery);

      // Assert
      expect(result.isOk()).toBe(true);
      if (result.isOk()) {
        expect(result.value).toEqual(expectedUser);
      }

      // Verify correct actor was called
      expect(daprClient.actors.getActor).toHaveBeenCalledWith(
        'AggregateActor',
        expect.any(String)
      );
    });

    it('should handle query not found errors', async () => {
      // Arrange
      const mockQuery = {
        queryType: 'GetTestUser',
        userId: 'non-existent-user'
      };

      // Mock actor response for non-existent entity
      const mockActorProxy = {
        queryAsync: vi.fn().mockRejectedValue(new Error('Entity not found'))
      };
      
      daprClient.actors.getActor = vi.fn().mockReturnValue(mockActorProxy);

      // Act
      const result = await executor.queryAsync(mockQuery);

      // Assert
      expect(result.isErr()).toBe(true);
      if (result.isErr()) {
        expect(result.error.message).toContain('Entity not found');
      }
    });
  });

  describe('Aggregate Loading', () => {
    it('should load aggregate through Dapr actor', async () => {
      // Arrange
      const partitionKeys: PartitionKeys = {
        aggregateId: 'user-123',
        partitionKey: 'TestUser',
        rootPartitionKey: 'default'
      };

      const expectedAggregate: Aggregate<TestUserPayload> = {
        partitionKeys,
        payload: {
          aggregateType: 'TestUser',
          id: 'user-123',
          name: 'John Doe',
          email: 'john@example.com'
        },
        version: 2,
        lastEventId: 'event-456',
        appliedEvents: []
      };

      // Mock actor response
      const mockActorProxy = {
        loadAggregateAsync: vi.fn().mockResolvedValue(expectedAggregate)
      };
      
      daprClient.actors.getActor = vi.fn().mockReturnValue(mockActorProxy);

      // Act
      const result = await executor.loadAggregateAsync(mockProjector, partitionKeys);

      // Assert
      expect(result.isOk()).toBe(true);
      if (result.isOk()) {
        expect(result.value).toEqual(expectedAggregate);
      }

      // Verify actor was called with correct parameters
      expect(daprClient.actors.getActor).toHaveBeenCalledWith(
        'AggregateActor',
        expect.stringContaining('user-123')
      );
      expect(mockActorProxy.loadAggregateAsync).toHaveBeenCalledWith(partitionKeys);
    });

    it('should return empty aggregate for non-existent partition', async () => {
      // Arrange
      const partitionKeys: PartitionKeys = {
        aggregateId: 'non-existent',
        partitionKey: 'TestUser',
        rootPartitionKey: 'default'
      };

      const emptyAggregate: Aggregate<EmptyAggregatePayload> = {
        partitionKeys,
        payload: { aggregateType: 'Empty' },
        version: 0,
        lastEventId: null,
        appliedEvents: []
      };

      // Mock actor response
      const mockActorProxy = {
        loadAggregateAsync: vi.fn().mockResolvedValue(emptyAggregate)
      };
      
      daprClient.actors.getActor = vi.fn().mockReturnValue(mockActorProxy);

      // Act
      const result = await executor.loadAggregateAsync(mockProjector, partitionKeys);

      // Assert
      expect(result.isOk()).toBe(true);
      if (result.isOk()) {
        expect(result.value.payload.aggregateType).toBe('Empty');
        expect(result.value.version).toBe(0);
      }
    });
  });

  describe('Configuration Management', () => {
    it('should provide access to Dapr configuration', () => {
      // Act & Assert
      expect(executor.getStateStoreName()).toBe('sekiban-eventstore');
      expect(executor.getPubSubName()).toBe('sekiban-pubsub');
      expect(executor.getEventTopicName()).toBe('domain-events');
    });

    it('should support updating configuration at runtime', () => {
      // Arrange
      const newConfig: Partial<DaprSekibanConfiguration> = {
        stateStoreName: 'new-eventstore',
        eventTopicName: 'new-events'
      };

      // Act
      executor.updateConfiguration(newConfig);

      // Assert
      expect(executor.getStateStoreName()).toBe('new-eventstore');
      expect(executor.getEventTopicName()).toBe('new-events');
      expect(executor.getPubSubName()).toBe('sekiban-pubsub'); // Unchanged
    });
  });
});