import { describe, it, expect, beforeEach, vi, MockedFunction } from 'vitest';
import { ok, err, Result } from 'neverthrow';
import type { DaprClient } from '@dapr/dapr';
import { HttpMethod } from '@dapr/dapr';
import type { 
  ICommandWithHandler,
  IAggregateProjector,
  ITypedAggregatePayload,
  EmptyAggregatePayload,
  Aggregate,
  ICommandContext,
  IEventPayload,
  Metadata,
  SekibanDomainTypes
} from '@sekiban/core';
import { PartitionKeys } from '@sekiban/core';
import { SekibanDaprExecutor, type DaprSekibanConfiguration } from './sekiban-dapr-executor.js';

// Mock types for testing
interface TestUserPayload extends ITypedAggregatePayload {
  readonly aggregateType: 'TestUser';
  id: string;
  name: string;
  email: string;
}

interface TestCreateUserCommandData {
  name: string;
  email: string;
}

class TestCreateUserCommand implements ICommandWithHandler<
  TestCreateUserCommandData,
  MockUserProjector,
  TestUserPayload,
  EmptyAggregatePayload
> {
  readonly commandType = 'CreateTestUser';
  
  specifyPartitionKeys(data: TestCreateUserCommandData): PartitionKeys {
    return new PartitionKeys('user-123', 'TestUser', 'default');
  }
  
  validate(data: TestCreateUserCommandData): Result<void, Error> {
    if (!data.name || !data.email) {
      return err(new Error('Invalid command data'));
    }
    return ok(undefined);
  }
  
  async handle(
    context: ICommandContext,
    data: TestCreateUserCommandData,
    aggregate: Aggregate<EmptyAggregatePayload>
  ): Promise<Result<IEventPayload[], Error>> {
    return ok([]);
  }
  
  getProjector(): MockUserProjector {
    return new MockUserProjector();
  }
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
  
  getVersion(): number {
    return 1;
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
  
  getVersion(): number {
    return 1;
  }
}

describe('SekibanDaprExecutor', () => {
  let daprClient: any;
  let configuration: DaprSekibanConfiguration;
  let executor: SekibanDaprExecutor;
  let mockProjector: MockUserProjector;
  let mockDomainTypes: SekibanDomainTypes;

  beforeEach(() => {
    // Mock Dapr client
    daprClient = {
      invoker: {
        invoke: vi.fn()
      },
      pubsub: {
        publish: vi.fn()
      }
    };

    // Configuration matching C# implementation
    configuration = {
      stateStoreName: 'sekiban-eventstore',
      pubSubName: 'sekiban-pubsub', 
      eventTopicName: 'domain-events',
      actorType: 'AggregateActor'
    };

    mockProjector = new MockUserProjector();

    // Mock domain types
    mockDomainTypes = {
      projectorTypes: {
        getProjectorByAggregateType: vi.fn().mockReturnValue(MockUserProjector),
        getProjectorTypes: vi.fn().mockReturnValue([{
          projector: MockUserProjector,
          aggregateTypeName: 'TestUser'
        }])
      },
      projectorRegistry: new Map([['MockUserProjector', MockUserProjector]])
    } as any;

    executor = new SekibanDaprExecutor(daprClient, mockDomainTypes, configuration);
  });

  describe('Construction', () => {
    it('should create executor with Dapr client and configuration', () => {
      // Act & Assert
      expect(executor).toBeDefined();
      expect(executor.getDaprClient()).toBe(daprClient);
      expect(executor.getConfiguration()).toEqual(configuration);
    });

    it('should use default actor type when not provided', () => {
      // Arrange
      const configWithoutActorType = { ...configuration };
      delete configWithoutActorType.actorType;

      // Act
      const executorWithDefault = new SekibanDaprExecutor(daprClient, mockDomainTypes, configWithoutActorType);

      // Assert
      expect(executorWithDefault.getConfiguration().actorType).toBe('AggregateActor');
    });

    it('should get registered projectors from domain types', () => {
      // Arrange
      const mockDomainTypesWithMultiple = {
        ...mockDomainTypes,
        projectorTypes: {
          getProjectorByAggregateType: vi.fn(),
          getProjectorTypes: vi.fn().mockReturnValue([
            { projector: MockUserProjector, aggregateTypeName: 'TestUser' },
            { projector: MockOrderProjector, aggregateTypeName: 'TestOrder' }
          ])
        }
      } as any;

      // Act
      const executorWithMultiple = new SekibanDaprExecutor(daprClient, mockDomainTypesWithMultiple, configuration);

      // Assert
      expect(executorWithMultiple.getRegisteredProjectors()).toHaveLength(2);
    });
  });

  describe('Command Execution', () => {
    it('should execute command through Dapr AggregateActor', async () => {
      // Arrange
      const mockCommand = new TestCreateUserCommand();
      const commandData: TestCreateUserCommandData = {
        name: 'John Doe',
        email: 'john@example.com'
      };

      const expectedResponse = {
        aggregateId: 'user-123',
        lastSortableUniqueId: '20250703T160000Z.123456.user-123',
        success: true
      };

      // Mock Dapr response
      daprClient.invoker.invoke.mockResolvedValue(expectedResponse);

      // Act
      const result = await executor.executeCommandAsync(mockCommand, commandData);

      // Assert
      expect(result.isOk()).toBe(true);
      if (result.isOk()) {
        expect(result.value).toEqual(expectedResponse);
      }

      // Verify Dapr was called correctly
      expect(daprClient.invoker.invoke).toHaveBeenCalledWith(
        expect.any(String),
        expect.stringContaining('actors/AggregateActor'),
        HttpMethod.POST,
        expect.objectContaining({
          command: mockCommand,
          commandData: commandData,
          partitionKeys: mockCommand.specifyPartitionKeys(commandData)
        })
      );
    });

    it('should return error when command validation fails', async () => {
      // Arrange
      const mockCommand = new TestCreateUserCommand();
      const invalidData: TestCreateUserCommandData = {
        name: '',
        email: ''
      };

      // Act
      const result = await executor.executeCommandAsync(mockCommand, invalidData);

      // Assert
      expect(result.isErr()).toBe(true);
      if (result.isErr()) {
        expect(result.error.message).toContain('Invalid command data');
      }

      // Verify no actor call was made
      expect(daprClient.invoker.invoke).not.toHaveBeenCalled();
    });

    it('should handle Dapr actor communication errors', async () => {
      // Arrange
      const mockCommand = new TestCreateUserCommand();
      const commandData: TestCreateUserCommandData = {
        name: 'John Doe',
        email: 'john@example.com'
      };

      // Mock actor communication failure
      daprClient.invoker.invoke.mockRejectedValue(new Error('Dapr actor unavailable'));

      // Act
      const result = await executor.executeCommandAsync(mockCommand, commandData);

      // Assert
      expect(result.isErr()).toBe(true);
      if (result.isErr()) {
        expect(result.error.message).toContain('Dapr actor unavailable');
      }
    });

    it('should retry on transient failures', async () => {
      // Arrange
      const mockCommand = new TestCreateUserCommand();
      const commandData: TestCreateUserCommandData = {
        name: 'John Doe',
        email: 'john@example.com'
      };

      const expectedResponse = {
        aggregateId: 'user-123',
        lastSortableUniqueId: '20250703T160000Z.123456.user-123',
        success: true
      };

      // Mock transient failure then success
      daprClient.invoker.invoke
        .mockRejectedValueOnce(new Error('Temporary failure'))
        .mockResolvedValueOnce(expectedResponse);

      // Act
      const result = await executor.executeCommandAsync(mockCommand, commandData);

      // Assert
      expect(result.isOk()).toBe(true);
      expect(daprClient.invoker.invoke).toHaveBeenCalledTimes(2);
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

      // Mock Dapr response
      daprClient.invoker.invoke.mockResolvedValue(expectedUser);

      // Act
      const result = await executor.queryAsync(mockQuery);

      // Assert
      expect(result.isOk()).toBe(true);
      if (result.isOk()) {
        expect(result.value).toEqual(expectedUser);
      }

      // Verify correct actor was called
      expect(daprClient.invoker.invoke).toHaveBeenCalledWith(
        expect.any(String),
        expect.stringContaining('actors/AggregateActor'),
        HttpMethod.POST,
        mockQuery
      );
    });

    it('should handle query not found errors', async () => {
      // Arrange
      const mockQuery = {
        queryType: 'GetTestUser',
        userId: 'non-existent-user'
      };

      // Mock actor response for non-existent entity
      daprClient.invoker.invoke.mockRejectedValue(new Error('Entity not found'));

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
      const partitionKeys = new PartitionKeys('user-123', 'TestUser', 'default');

      const expectedAggregate: Aggregate<TestUserPayload> = {
        partitionKeys,
        aggregateType: 'TestUser',
        payload: {
          aggregateType: 'TestUser',
          id: 'user-123',
          name: 'John Doe',
          email: 'john@example.com'
        },
        version: 2,
        lastSortableUniqueId: null,
        projectorName: 'MockUserProjector',
        projectorVersion: 1
      } as any;

      // Mock Dapr response
      daprClient.invoker.invoke.mockResolvedValue(expectedAggregate);

      // Act
      const result = await executor.loadAggregateAsync(mockProjector, partitionKeys);

      // Assert
      expect(result.isOk()).toBe(true);
      if (result.isOk()) {
        expect(result.value).toEqual(expectedAggregate);
      }

      // Verify actor was called with correct parameters
      expect(daprClient.invoker.invoke).toHaveBeenCalledWith(
        expect.any(String),
        expect.stringContaining('actors/AggregateActor'),
        HttpMethod.POST,
        partitionKeys
      );
    });

    it('should return error for unregistered projector', async () => {
      // Arrange
      const partitionKeys = new PartitionKeys('non-existent', 'UnknownType', 'default');

      const unknownProjector = {
        aggregateTypeName: 'UnknownType'
      } as any;

      // Mock domain types to return null for unknown projector
      mockDomainTypes.projectorTypes.getProjectorByAggregateType = vi.fn().mockReturnValue(null);

      // Act
      const result = await executor.loadAggregateAsync(unknownProjector, partitionKeys);

      // Assert
      expect(result.isErr()).toBe(true);
      if (result.isErr()) {
        expect(result.error.message).toContain('No projector registered');
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

    it('should return domain types', () => {
      // Act
      const domainTypes = executor.getDomainTypes();

      // Assert
      expect(domainTypes).toBe(mockDomainTypes);
    });
  });
});