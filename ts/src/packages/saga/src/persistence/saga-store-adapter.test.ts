import { describe, it, expect, beforeEach } from 'vitest';
import { SagaStoreAdapter, createSagaStore } from './saga-store-adapter';
import { InMemorySagaRepository } from './in-memory-saga-repository';
import { SagaInstance, SagaDefinition, SagaStatus, CompensationStrategy } from '../types';
import { PartitionKeys, SortableUniqueId } from '../../core/src';

// Test saga context
interface TestSagaContext {
  orderId: string;
  customerId: string;
  amount: number;
  currentStep?: string;
}

describe('SagaStoreAdapter', () => {
  let repository: InMemorySagaRepository;
  let adapter: SagaStoreAdapter<TestSagaContext>;

  const testDefinition: SagaDefinition<TestSagaContext> = {
    name: 'TestSaga',
    version: 1,
    trigger: { eventType: 'TestEvent' },
    steps: [{
      name: 'Step1',
      command: (context) => ({ type: 'TestCommand', payload: context }),
      onSuccess: (context, event) => ({ ...context, currentStep: 'step1' })
    }],
    compensationStrategy: CompensationStrategy.Backward,
    timeout: 300000
  };

  beforeEach(() => {
    repository = new InMemorySagaRepository();
    adapter = new SagaStoreAdapter<TestSagaContext>(repository);
  });

  describe('Instance to Snapshot Conversion', () => {
    it('should convert SagaInstance to SagaSnapshot correctly', async () => {
      const instance: SagaInstance<TestSagaContext> = {
        id: 'saga-123',
        definition: testDefinition,
        state: {
          sagaId: 'saga-123',
          sagaType: 'TestSaga',
          status: SagaStatus.Running,
          currentStep: 1,
          startedAt: new Date(),
          context: {
            orderId: 'order-456',
            customerId: 'customer-789',
            amount: 100
          },
          completedSteps: ['Step1'],
          failedStep: null,
          error: null
        },
        events: [],
        createdAt: new Date(),
        updatedAt: new Date()
      };

      const result = await adapter.save(instance);
      expect(result.isOk()).toBe(true);

      // Verify the snapshot was saved correctly
      const snapshot = await repository.load('saga-123');
      expect(snapshot).not.toBeNull();
      expect(snapshot!.id).toBe('saga-123');
      expect(snapshot!.sagaType).toBe('TestSaga');
      expect(snapshot!.state.status).toBe(SagaStatus.Running);
      expect(snapshot!.state.context.orderId).toBe('order-456');
    });

    it('should calculate expiration time based on saga timeout', async () => {
      const startTime = new Date();
      const instance: SagaInstance<TestSagaContext> = {
        id: 'saga-with-timeout',
        definition: testDefinition,
        state: {
          sagaId: 'saga-with-timeout',
          sagaType: 'TestSaga',
          status: SagaStatus.Running,
          currentStep: 0,
          startedAt: startTime,
          context: { orderId: 'order-1', customerId: 'customer-1', amount: 100 },
          completedSteps: [],
          failedStep: null,
          error: null
        },
        events: [],
        createdAt: startTime,
        updatedAt: startTime
      };

      await adapter.save(instance);
      const snapshot = await repository.load('saga-with-timeout');
      
      expect(snapshot!.expiresAt).toBeDefined();
      const expectedExpiration = new Date(startTime.getTime() + testDefinition.timeout!);
      expect(snapshot!.expiresAt!.getTime()).toBeCloseTo(expectedExpiration.getTime(), -2);
    });
  });

  describe('Snapshot to Instance Conversion', () => {
    it('should convert SagaSnapshot back to SagaInstance correctly', async () => {
      const originalInstance: SagaInstance<TestSagaContext> = {
        id: 'saga-456',
        definition: testDefinition,
        state: {
          sagaId: 'saga-456',
          sagaType: 'TestSaga',
          status: SagaStatus.Completed,
          currentStep: 1,
          startedAt: new Date(),
          context: {
            orderId: 'order-789',
            customerId: 'customer-123',
            amount: 200
          },
          completedSteps: ['Step1'],
          failedStep: null,
          error: null
        },
        events: [],
        createdAt: new Date(),
        updatedAt: new Date()
      };

      // Save and then load
      await adapter.save(originalInstance);
      const loadResult = await adapter.load('saga-456');

      expect(loadResult.isOk()).toBe(true);
      const loadedInstance = loadResult.value!;
      
      expect(loadedInstance.id).toBe('saga-456');
      expect(loadedInstance.state.status).toBe(SagaStatus.Completed);
      expect(loadedInstance.state.context.orderId).toBe('order-789');
      expect(loadedInstance.state.completedSteps).toEqual(['Step1']);
    });

    it('should return null for non-existent saga', async () => {
      const result = await adapter.load('non-existent');
      expect(result.isOk()).toBe(true);
      expect(result.value).toBeNull();
    });
  });

  describe('List Operations', () => {
    beforeEach(async () => {
      // Create test sagas with different statuses
      const runningSaga: SagaInstance<TestSagaContext> = {
        id: 'running-saga',
        definition: testDefinition,
        state: {
          sagaId: 'running-saga',
          sagaType: 'TestSaga',
          status: SagaStatus.Running,
          currentStep: 0,
          startedAt: new Date(),
          context: { orderId: 'order-1', customerId: 'customer-1', amount: 100 },
          completedSteps: [],
          failedStep: null,
          error: null
        },
        events: [],
        createdAt: new Date(),
        updatedAt: new Date()
      };

      const completedSaga: SagaInstance<TestSagaContext> = {
        id: 'completed-saga',
        definition: { ...testDefinition, name: 'AnotherSaga' },
        state: {
          sagaId: 'completed-saga',
          sagaType: 'AnotherSaga',
          status: SagaStatus.Completed,
          currentStep: 1,
          startedAt: new Date(),
          context: { orderId: 'order-2', customerId: 'customer-2', amount: 200 },
          completedSteps: ['Step1'],
          failedStep: null,
          error: null
        },
        events: [],
        createdAt: new Date(),
        updatedAt: new Date()
      };

      await adapter.save(runningSaga);
      await adapter.save(completedSaga);
    });

    it('should list all sagas without filter', async () => {
      const result = await adapter.list();
      expect(result.isOk()).toBe(true);
      
      const sagas = result.value!;
      expect(sagas).toHaveLength(2);
      expect(sagas.map(s => s.id)).toContain('running-saga');
      expect(sagas.map(s => s.id)).toContain('completed-saga');
    });

    it('should filter sagas by status', async () => {
      const runningResult = await adapter.list({ status: SagaStatus.Running });
      expect(runningResult.isOk()).toBe(true);
      
      const runningSagas = runningResult.value!;
      expect(runningSagas).toHaveLength(1);
      expect(runningSagas[0].id).toBe('running-saga');

      const completedResult = await adapter.list({ status: SagaStatus.Completed });
      expect(completedResult.isOk()).toBe(true);
      
      const completedSagas = completedResult.value!;
      expect(completedSagas).toHaveLength(1);
      expect(completedSagas[0].id).toBe('completed-saga');
    });

    it('should filter sagas by type', async () => {
      const testSagaResult = await adapter.list({ sagaType: 'TestSaga' });
      expect(testSagaResult.isOk()).toBe(true);
      
      const testSagas = testSagaResult.value!;
      expect(testSagas).toHaveLength(1);
      expect(testSagas[0].id).toBe('running-saga');
    });
  });

  describe('Event Management', () => {
    it('should save and retrieve saga events', async () => {
      const event = {
        sagaId: 'saga-with-events',
        eventType: 'SagaStepCompleted',
        timestamp: new Date(),
        data: { stepName: 'Step1', result: 'success' }
      };

      const result = await adapter.saveEvent(event);
      expect(result.isOk()).toBe(true);

      const events = adapter.getEvents('saga-with-events');
      expect(events).toHaveLength(1);
      expect(events[0].eventType).toBe('SagaStepCompleted');
    });

    it('should clear events for a saga', async () => {
      const event = {
        sagaId: 'saga-to-clear',
        eventType: 'TestEvent',
        timestamp: new Date(),
        data: {}
      };

      await adapter.saveEvent(event);
      expect(adapter.getEvents('saga-to-clear')).toHaveLength(1);

      adapter.clearEvents('saga-to-clear');
      expect(adapter.getEvents('saga-to-clear')).toHaveLength(0);
    });
  });
});

describe('createSagaStore Factory', () => {
  it('should create a SagaStore interface that works with SagaManager', async () => {
    const repository = new InMemorySagaRepository();
    const sagaStore = createSagaStore<TestSagaContext>(repository);

    const instance: SagaInstance<TestSagaContext> = {
      id: 'factory-test-saga',
      definition: {
        name: 'FactoryTestSaga',
        version: 1,
        trigger: { eventType: 'TestEvent' },
        steps: [],
        compensationStrategy: CompensationStrategy.Backward
      },
      state: {
        sagaId: 'factory-test-saga',
        sagaType: 'FactoryTestSaga',
        status: SagaStatus.Running,
        currentStep: 0,
        startedAt: new Date(),
        context: { orderId: 'order-1', customerId: 'customer-1', amount: 100 },
        completedSteps: [],
        failedStep: null,
        error: null
      },
      events: [],
      createdAt: new Date(),
      updatedAt: new Date()
    };

    // Test save
    const saveResult = await sagaStore.save(instance);
    expect(saveResult.isOk()).toBe(true);

    // Test load
    const loadResult = await sagaStore.load('factory-test-saga');
    expect(loadResult.isOk()).toBe(true);
    expect(loadResult.value).not.toBeNull();
    expect(loadResult.value!.id).toBe('factory-test-saga');

    // Test list
    const listResult = await sagaStore.list();
    expect(listResult.isOk()).toBe(true);
    expect(listResult.value!).toHaveLength(1);

    // Test saveEvent
    const eventResult = await sagaStore.saveEvent({
      sagaId: 'factory-test-saga',
      eventType: 'TestEvent',
      timestamp: new Date(),
      data: {}
    });
    expect(eventResult.isOk()).toBe(true);
  });
});