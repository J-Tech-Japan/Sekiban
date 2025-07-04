import { describe, it, expect, beforeEach, vi } from 'vitest';
import { Actor, ActorHost } from '@dapr/dapr';
import { AggregateActor } from '../../src/actors/aggregate-actor.js';
import { PartitionKeys, EmptyAggregatePayload, Aggregate, SortableUniqueId } from '@sekiban/core';
import { PartitionKeysAndProjector } from '../../src/parts/partition-keys-and-projector.js';
import type { IAggregateEventHandlerActor } from '../../src/actors/interfaces.js';
import { WeatherForecastProjector } from '../test-fixtures/weather-forecast-projector.js';

describe('AggregateActor', () => {
  let actor: AggregateActor;
  let mockHost: ActorHost;
  let mockStateManager: any;
  let mockActorProxyFactory: any;
  let mockEventHandlerActor: IAggregateEventHandlerActor;
  let mockDomainTypes: any;
  let mockServiceProvider: any;
  let mockSerializationService: any;

  beforeEach(() => {
    // Setup mock state manager
    mockStateManager = {
      tryGetState: vi.fn(),
      setState: vi.fn(),
      getState: vi.fn()
    };

    // Setup mock host
    mockHost = {
      id: { id: 'default@WeatherForecast@123=WeatherForecastProjector' },
      stateManager: mockStateManager
    } as unknown as ActorHost;

    // Setup mock event handler actor
    mockEventHandlerActor = {
      appendEventsAsync: vi.fn().mockResolvedValue({ isSuccess: true }),
      getAllEventsAsync: vi.fn().mockResolvedValue([]),
      getDeltaEventsAsync: vi.fn().mockResolvedValue([])
    } as unknown as IAggregateEventHandlerActor;

    // Setup mock actor proxy factory
    mockActorProxyFactory = {
      createActorProxy: vi.fn().mockReturnValue(mockEventHandlerActor)
    };

    // Setup mock domain types
    mockDomainTypes = {
      projectorRegistry: new Map(),
      eventTypes: {},
      commandTypes: {},
      jsonSerializerOptions: {}
    };

    mockServiceProvider = {};
    mockSerializationService = {
      deserializeAggregateAsync: vi.fn().mockResolvedValue(null),
      serializeAggregateAsync: vi.fn().mockImplementation(async (aggregate) => ({
        // Return a simple surrogate object
        aggregateData: aggregate
      }))
    };

    actor = new AggregateActor(
      mockHost,
      mockDomainTypes,
      mockServiceProvider,
      mockActorProxyFactory,
      mockSerializationService
    );
  });

  describe('Partition Info Extraction', () => {
    it('should extract partition info from actor ID on initialization', async () => {
      // Test 1: The actor should parse its ID to get PartitionKeysAndProjector
      const grainKey = 'default@WeatherForecast@123=WeatherForecastProjector';
      mockHost.id.id = grainKey;

      // Mock no saved partition info - tryGetState returns { hasValue, value }
      mockStateManager.tryGetState.mockResolvedValue({ hasValue: false, value: null });

      // Register the projector class
      mockDomainTypes.projectorRegistry.set('WeatherForecastProjector', WeatherForecastProjector);

      // Call ensureInitializedAsync (private method - we'll test through executeCommandAsync)
      const mockCommand = {
        validate: vi.fn().mockReturnValue({ 
          isOk: () => true, 
          isErr: () => false 
        }),
        specifyPartitionKeys: vi.fn().mockReturnValue(
          PartitionKeys.create('123', 'WeatherForecast', 'default')
        ),
        handle: vi.fn().mockReturnValue({ 
          isOk: () => true,
          isErr: () => false,
          value: [] // No events produced
        })
      };

      // This should trigger initialization
      await actor.executeCommandAsync({
        command: mockCommand,
        partitionKeys: PartitionKeys.create('123', 'WeatherForecast', 'default'),
        metadata: { timestamp: new Date().toISOString(), requestId: '123' }
      });

      // Verify state manager was called to save partition info
      expect(mockStateManager.setState).toHaveBeenCalledWith(
        'partitionInfo',
        { grainKey }
      );
    });
  });

  describe('Event Handler Actor Creation', () => {
    it('should create event handler actor with correct ID format', async () => {
      // Test 2: Event handler actor ID should be "eventhandler:{ToEventHandlerGrainKey()}"
      const grainKey = 'default@WeatherForecast@123=WeatherForecastProjector';
      mockHost.id.id = grainKey;

      // Register the projector class
      mockDomainTypes.projectorRegistry.set('WeatherForecastProjector', WeatherForecastProjector);

      // Mock state manager for this test
      mockStateManager.tryGetState.mockResolvedValue({ hasValue: false, value: null });

      // Execute a command to trigger event handler creation
      const mockCommand = {
        validate: vi.fn().mockReturnValue({ 
          isOk: () => true,
          isErr: () => false
        }),
        specifyPartitionKeys: vi.fn().mockReturnValue(
          PartitionKeys.create('123', 'WeatherForecast', 'default')
        ),
        handle: vi.fn().mockReturnValue({ 
          isOk: () => true,
          isErr: () => false,
          value: [] 
        })
      };

      await actor.executeCommandAsync({
        command: mockCommand,
        partitionKeys: PartitionKeys.create('123', 'WeatherForecast', 'default'),
        metadata: { timestamp: new Date().toISOString(), requestId: '123' }
      });

      // Verify actor proxy factory was called with correct ID
      // ToEventHandlerGrainKey() returns just the partition keys string without projector
      expect(mockActorProxyFactory.createActorProxy).toHaveBeenCalledWith(
        { id: 'eventhandler:default@WeatherForecast@123' },
        'AggregateEventHandlerActor'
      );
    });
  });

  describe('DaprRepository Integration', () => {
    it('should create DaprRepository with correct parameters', async () => {
      // Test 3: DaprRepository should be created with event handler actor and partition info
      const grainKey = 'default@WeatherForecast@123=WeatherForecastProjector';
      mockHost.id.id = grainKey;

      // Register the projector class
      mockDomainTypes.projectorRegistry.set('WeatherForecastProjector', WeatherForecastProjector);

      // Mock state manager for this test
      mockStateManager.tryGetState.mockResolvedValue({ hasValue: false, value: null });

      const mockCommand = {
        validate: vi.fn().mockReturnValue({ 
          isOk: () => true,
          isErr: () => false
        }),
        specifyPartitionKeys: vi.fn().mockReturnValue(
          PartitionKeys.create('123', 'WeatherForecast', 'default')
        ),
        handle: vi.fn().mockReturnValue({ 
          isOk: () => true,
          isErr: () => false, 
          value: [{
            location: 'Tokyo'
          }]
        })
      };

      await actor.executeCommandAsync({
        command: mockCommand,
        partitionKeys: PartitionKeys.create('123', 'WeatherForecast', 'default'),
        metadata: { timestamp: new Date().toISOString(), requestId: '123' }
      });

      // Verify event handler actor was called to append events
      expect(mockEventHandlerActor.appendEventsAsync).toHaveBeenCalled();
    });
  });

  describe('State Management', () => {
    it('should save aggregate state after command execution with events', async () => {
      // Test 4: State should be saved when events are produced
      const grainKey = 'default@WeatherForecast@123=WeatherForecastProjector';
      mockHost.id.id = grainKey;

      // Register the projector class
      mockDomainTypes.projectorRegistry.set('WeatherForecastProjector', WeatherForecastProjector);

      // Mock state manager for this test
      mockStateManager.tryGetState.mockResolvedValue({ hasValue: false, value: null });

      const mockCommand = {
        validate: vi.fn().mockReturnValue({ 
          isOk: () => true,
          isErr: () => false
        }),
        specifyPartitionKeys: vi.fn().mockReturnValue(
          PartitionKeys.create('123', 'WeatherForecast', 'default')
        ),
        handle: vi.fn().mockReturnValue({ 
          isOk: () => true,
          isErr: () => false, 
          value: [{
            location: 'Tokyo'
          }]
        })
      };

      await actor.executeCommandAsync({
        command: mockCommand,
        partitionKeys: PartitionKeys.create('123', 'WeatherForecast', 'default'),
        metadata: { timestamp: new Date().toISOString(), requestId: '123' }
      });

      // Manually trigger state save callback since timer won't fire in test
      await actor.saveStateCallbackAsync();

      // Verify state was saved
      expect(mockStateManager.setState).toHaveBeenCalledWith(
        'aggregateState',
        expect.any(Object)
      );
    });

    it('should not save state if no events are produced', async () => {
      // Test 5: State should not be saved when no events are produced
      const grainKey = 'default@WeatherForecast@123=WeatherForecastProjector';
      mockHost.id.id = grainKey;

      // Register the projector class
      mockDomainTypes.projectorRegistry.set('WeatherForecastProjector', WeatherForecastProjector);

      // Mock state manager for this test
      mockStateManager.tryGetState.mockResolvedValue({ hasValue: false, value: null });

      const mockCommand = {
        validate: vi.fn().mockReturnValue({ 
          isOk: () => true,
          isErr: () => false
        }),
        specifyPartitionKeys: vi.fn().mockReturnValue(
          PartitionKeys.create('123', 'WeatherForecast', 'default')
        ),
        handle: vi.fn().mockReturnValue({ 
          isOk: () => true,
          isErr: () => false, 
          value: [] // No events
        })
      };

      // Reset setState mock to track only calls after command execution
      mockStateManager.setState.mockClear();

      await actor.executeCommandAsync({
        command: mockCommand,
        partitionKeys: PartitionKeys.create('123', 'WeatherForecast', 'default'),
        metadata: { timestamp: new Date().toISOString(), requestId: '123' }
      });

      // Verify state was NOT saved for aggregate (only partition info should be saved)
      const aggregateStateCalls = mockStateManager.setState.mock.calls.filter(
        call => call[0] === 'aggregateState'
      );
      expect(aggregateStateCalls).toHaveLength(0);
    });
  });
});