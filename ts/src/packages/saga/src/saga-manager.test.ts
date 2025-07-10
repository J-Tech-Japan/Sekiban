import { describe, it, expect, vi, beforeEach } from 'vitest';
import { SagaManager } from './saga-manager';
import { 
  SagaDefinition, 
  SagaStatus, 
  CompensationStrategy,
  SagaInstance,
  SagaEvent
} from './types';
import { 
  ICommand, 
  IEventPayload, 
  EventDocument,
  PartitionKeys,
  SortableUniqueId
} from '../../core/src';
import { ok, err } from '../../core/src/result';
import { SagaError, SagaTimeoutError } from './errors';

// Test events and commands
interface OrderPlaced extends IEventPayload {
  orderId: string;
  customerId: string;
  totalAmount: number;
  items: Array<{ productId: string; quantity: number }>;
}

interface PaymentProcessed extends IEventPayload {
  orderId: string;
  paymentId: string;
  amount: number;
}

interface ProcessPayment extends ICommand {
  orderId: string;
  amount: number;
}

interface CancelPayment extends ICommand {
  paymentId: string;
  reason: string;
}

// Test saga context
interface OrderSagaContext {
  orderId: string;
  customerId: string;
  totalAmount: number;
  paymentId?: string;
  currentStep?: string;
}

describe('SagaManager', () => {
  let sagaManager: SagaManager;
  let mockCommandExecutor: any;
  let mockSagaStore: any;
  
  const createTestEvent = (payload: OrderPlaced): EventDocument<OrderPlaced> => ({
    id: 'event-123',
    eventType: 'OrderPlaced',
    payload,
    version: 1,
    timestamp: new Date(),
    sortableUniqueId: SortableUniqueId.generate(),
    partitionKeys: PartitionKeys.create('order-123', 'orders')
  });

  const orderSaga: SagaDefinition<OrderSagaContext> = {
    name: 'OrderFulfillmentSaga',
    version: 1,
    trigger: {
      eventType: 'OrderPlaced',
      filter: (event) => (event.payload as OrderPlaced).totalAmount > 0
    },
    steps: [
      {
        name: 'ProcessPayment',
        command: (context) => ({
          type: 'ProcessPayment',
          payload: {
            orderId: context.orderId,
            amount: context.totalAmount
          }
        } as ProcessPayment & { type: string }),
        compensation: (context) => ({
          type: 'CancelPayment',
          payload: {
            paymentId: context.paymentId!,
            reason: 'Saga failed'
          }
        } as CancelPayment & { type: string }),
        onSuccess: (context, event) => ({
          ...context,
          paymentId: (event?.payload as PaymentProcessed).paymentId
        })
      }
    ],
    compensationStrategy: CompensationStrategy.Backward,
    initialContext: (trigger) => {
      const payload = trigger.payload as OrderPlaced;
      return {
        orderId: payload.orderId,
        customerId: payload.customerId,
        totalAmount: payload.totalAmount
      };
    }
  };

  beforeEach(() => {
    mockCommandExecutor = {
      execute: vi.fn()
    };
    
    mockSagaStore = {
      save: vi.fn(),
      load: vi.fn(),
      list: vi.fn(),
      saveEvent: vi.fn()
    };
    
    sagaManager = new SagaManager({
      commandExecutor: mockCommandExecutor,
      sagaStore: mockSagaStore
    });
  });

  describe('Saga Registration', () => {
    it('should register a saga definition', () => {
      sagaManager.register(orderSaga);
      
      const registered = sagaManager.getSagaDefinition('OrderFulfillmentSaga');
      expect(registered).toBe(orderSaga);
    });

    it('should handle multiple saga registrations', () => {
      const anotherSaga: SagaDefinition<any> = {
        ...orderSaga,
        name: 'ShippingSaga'
      };
      
      sagaManager.register(orderSaga);
      sagaManager.register(anotherSaga);
      
      expect(sagaManager.getSagaDefinition('OrderFulfillmentSaga')).toBeDefined();
      expect(sagaManager.getSagaDefinition('ShippingSaga')).toBeDefined();
    });
  });

  describe('Saga Triggering', () => {
    it('should start a saga when trigger event matches', async () => {
      sagaManager.register(orderSaga);
      
      const event = createTestEvent({
        orderId: 'order-123',
        customerId: 'customer-456',
        totalAmount: 100,
        items: [{ productId: 'prod-1', quantity: 2 }]
      });

      mockSagaStore.save.mockResolvedValue(ok(undefined));
      mockSagaStore.saveEvent.mockResolvedValue(ok(undefined));
      
      const result = await sagaManager.handleEvent(event);
      
      expect(result.isOk()).toBe(true);
      expect(mockSagaStore.save).toHaveBeenCalledWith(
        expect.objectContaining({
          definition: orderSaga,
          state: expect.objectContaining({
            status: SagaStatus.Running,
            context: expect.objectContaining({
              orderId: 'order-123',
              customerId: 'customer-456',
              totalAmount: 100
            })
          })
        })
      );
    });

    it('should not start saga when filter returns false', async () => {
      const filteredSaga: SagaDefinition<OrderSagaContext> = {
        ...orderSaga,
        trigger: {
          eventType: 'OrderPlaced',
          filter: (event) => (event.payload as OrderPlaced).totalAmount > 1000
        }
      };
      
      sagaManager.register(filteredSaga);
      
      const event = createTestEvent({
        orderId: 'order-123',
        customerId: 'customer-456',
        totalAmount: 100, // Below threshold
        items: []
      });
      
      const result = await sagaManager.handleEvent(event);
      
      expect(result.isOk()).toBe(true);
      expect(mockSagaStore.save).not.toHaveBeenCalled();
    });
  });

  describe('Saga Execution', () => {
    it('should execute saga steps sequentially', async () => {
      const sagaInstance: SagaInstance<OrderSagaContext> = {
        id: 'saga-123',
        definition: orderSaga,
        state: {
          sagaId: 'saga-123',
          sagaType: 'OrderFulfillmentSaga',
          status: SagaStatus.Running,
          currentStep: 0,
          startedAt: new Date(),
          context: {
            orderId: 'order-123',
            customerId: 'customer-456',
            totalAmount: 100
          },
          completedSteps: [],
          failedStep: null,
          error: null
        },
        events: [],
        createdAt: new Date(),
        updatedAt: new Date()
      };

      mockSagaStore.load.mockResolvedValue(ok(sagaInstance));
      mockSagaStore.save.mockResolvedValue(ok(undefined));
      mockSagaStore.saveEvent.mockResolvedValue(ok(undefined));
      
      const paymentEvent: EventDocument<PaymentProcessed> = {
        id: 'event-456',
        eventType: 'PaymentProcessed',
        payload: {
          orderId: 'order-123',
          paymentId: 'payment-789',
          amount: 100
        },
        version: 1,
        timestamp: new Date(),
        sortableUniqueId: SortableUniqueId.generate()
      };
      
      mockCommandExecutor.execute.mockResolvedValue(ok([paymentEvent]));
      
      const result = await sagaManager.executeNextStep('saga-123');
      
      expect(result.isOk()).toBe(true);
      expect(mockCommandExecutor.execute).toHaveBeenCalledWith(
        expect.objectContaining({
          type: 'ProcessPayment',
          payload: {
            orderId: 'order-123',
            amount: 100
          }
        })
      );
      
      expect(mockSagaStore.save).toHaveBeenCalledWith(
        expect.objectContaining({
          state: expect.objectContaining({
            completedSteps: ['ProcessPayment'],
            context: expect.objectContaining({
              paymentId: 'payment-789'
            })
          })
        })
      );
    });

    it('should handle step failures and start compensation', async () => {
      const sagaInstance: SagaInstance<OrderSagaContext> = {
        id: 'saga-123',
        definition: orderSaga,
        state: {
          sagaId: 'saga-123',
          sagaType: 'OrderFulfillmentSaga',
          status: SagaStatus.Running,
          currentStep: 0,
          startedAt: new Date(),
          context: {
            orderId: 'order-123',
            customerId: 'customer-456',
            totalAmount: 100
          },
          completedSteps: [],
          failedStep: null,
          error: null
        },
        events: [],
        createdAt: new Date(),
        updatedAt: new Date()
      };

      mockSagaStore.load.mockResolvedValue(ok(sagaInstance));
      mockSagaStore.save.mockResolvedValue(ok(undefined));
      mockSagaStore.saveEvent.mockResolvedValue(ok(undefined));
      
      const error = new Error('Payment failed');
      mockCommandExecutor.execute.mockResolvedValue(err(error));
      
      const result = await sagaManager.executeNextStep('saga-123');
      
      expect(result.isOk()).toBe(true);
      // Check that saga was eventually set to compensated status
      expect(mockSagaStore.save).toHaveBeenCalled();
      
      // Find the save call with compensated status
      const compensatedSave = mockSagaStore.save.mock.calls.find((call: any) => 
        call[0].state.status === SagaStatus.Compensated
      );
      expect(compensatedSave).toBeDefined();
      expect(compensatedSave[0].state.failedStep).toBe('ProcessPayment');
      expect(compensatedSave[0].state.error).toBe(error);
    });
  });

  describe('Compensation', () => {
    it('should compensate completed steps in backward order', async () => {
      const sagaWithMultipleSteps: SagaDefinition<OrderSagaContext> = {
        ...orderSaga,
        steps: [
          orderSaga.steps[0],
          {
            name: 'ReserveInventory',
            command: (context) => ({
              type: 'ReserveInventory',
              payload: { orderId: context.orderId }
            }),
            compensation: (context) => ({
              type: 'ReleaseInventory',
              payload: { orderId: context.orderId }
            }),
            onSuccess: (context) => context
          }
        ]
      };

      const sagaInstance: SagaInstance<OrderSagaContext> = {
        id: 'saga-123',
        definition: sagaWithMultipleSteps,
        state: {
          sagaId: 'saga-123',
          sagaType: 'OrderFulfillmentSaga',
          status: SagaStatus.Compensating,
          currentStep: 1,
          startedAt: new Date(),
          context: {
            orderId: 'order-123',
            customerId: 'customer-456',
            totalAmount: 100,
            paymentId: 'payment-789'
          },
          completedSteps: ['ProcessPayment'],
          failedStep: 'ReserveInventory',
          error: new Error('Out of stock'),
          compensatedSteps: []
        },
        events: [],
        createdAt: new Date(),
        updatedAt: new Date()
      };

      mockSagaStore.load.mockResolvedValue(ok(sagaInstance));
      mockSagaStore.save.mockResolvedValue(ok(undefined));
      mockSagaStore.saveEvent.mockResolvedValue(ok(undefined));
      mockCommandExecutor.execute.mockResolvedValue(ok([]));
      
      const result = await sagaManager.compensate('saga-123');
      
      expect(result.isOk()).toBe(true);
      expect(mockCommandExecutor.execute).toHaveBeenCalledWith(
        expect.objectContaining({
          type: 'CancelPayment',
          payload: {
            paymentId: 'payment-789',
            reason: 'Saga failed'
          }
        })
      );
    });
  });

  describe('Saga Lifecycle Events', () => {
    it('should emit lifecycle events', async () => {
      sagaManager.register(orderSaga);
      
      const event = createTestEvent({
        orderId: 'order-123',
        customerId: 'customer-456',
        totalAmount: 100,
        items: []
      });

      mockSagaStore.save.mockResolvedValue(ok(undefined));
      mockSagaStore.saveEvent.mockResolvedValue(ok(undefined));
      
      await sagaManager.handleEvent(event);
      
      expect(mockSagaStore.saveEvent).toHaveBeenCalledWith(
        expect.objectContaining({
          eventType: 'SagaStarted',
          data: expect.objectContaining({
            sagaType: 'OrderFulfillmentSaga',
            trigger: expect.objectContaining({
              eventType: 'OrderPlaced'
            })
          })
        })
      );
    });
  });

  describe('Retry and Timeout', () => {
    it('should retry failed steps according to retry policy', async () => {
      const sagaWithRetry: SagaDefinition<OrderSagaContext> = {
        ...orderSaga,
        steps: [{
          ...orderSaga.steps[0],
          retryPolicy: {
            maxAttempts: 3,
            backoffMs: 100,
            exponential: true
          }
        }]
      };

      const sagaInstance: SagaInstance<OrderSagaContext> = {
        id: 'saga-123',
        definition: sagaWithRetry,
        state: {
          sagaId: 'saga-123',
          sagaType: 'OrderFulfillmentSaga',
          status: SagaStatus.Running,
          currentStep: 0,
          startedAt: new Date(),
          context: {
            orderId: 'order-123',
            customerId: 'customer-456',
            totalAmount: 100
          },
          completedSteps: [],
          failedStep: null,
          error: null
        },
        events: [],
        createdAt: new Date(),
        updatedAt: new Date()
      };

      mockSagaStore.load.mockResolvedValue(ok(sagaInstance));
      mockSagaStore.save.mockResolvedValue(ok(undefined));
      mockSagaStore.saveEvent.mockResolvedValue(ok(undefined));
      
      // First two attempts fail, third succeeds
      const successEvent: EventDocument<PaymentProcessed> = {
        id: 'event-456',
        eventType: 'PaymentProcessed',
        payload: {
          orderId: 'order-123',
          paymentId: 'payment-789',
          amount: 100
        },
        version: 1,
        timestamp: new Date(),
        sortableUniqueId: SortableUniqueId.generate(),
        partitionKeys: PartitionKeys.create('order-123', 'orders')
      };
      
      mockCommandExecutor.execute
        .mockResolvedValueOnce(err(new Error('Temporary failure')))
        .mockResolvedValueOnce(err(new Error('Temporary failure')))
        .mockResolvedValueOnce(ok([successEvent]));
      
      const result = await sagaManager.executeNextStep('saga-123');
      
      expect(result.isOk()).toBe(true);
      expect(mockCommandExecutor.execute).toHaveBeenCalledTimes(3);
    });

    it('should timeout saga after specified duration', async () => {
      const sagaWithTimeout: SagaDefinition<OrderSagaContext> = {
        ...orderSaga,
        timeout: 100 // 100ms for testing
      };

      const oldDate = new Date();
      oldDate.setMinutes(oldDate.getMinutes() - 10); // 10 minutes ago

      const sagaInstance: SagaInstance<OrderSagaContext> = {
        id: 'saga-123',
        definition: sagaWithTimeout,
        state: {
          sagaId: 'saga-123',
          sagaType: 'OrderFulfillmentSaga',
          status: SagaStatus.Running,
          currentStep: 0,
          startedAt: oldDate,
          context: {
            orderId: 'order-123',
            customerId: 'customer-456',
            totalAmount: 100
          },
          completedSteps: [],
          failedStep: null,
          error: null
        },
        events: [],
        createdAt: oldDate,
        updatedAt: oldDate
      };

      mockSagaStore.load.mockResolvedValue(ok(sagaInstance));
      mockSagaStore.save.mockResolvedValue(ok(undefined));
      mockSagaStore.saveEvent.mockResolvedValue(ok(undefined));
      
      const result = await sagaManager.checkTimeout('saga-123');
      
      expect(result.isErr()).toBe(true);
      if (result.isErr()) {
        expect(result.error).toBeInstanceOf(SagaTimeoutError);
      }
      expect(mockSagaStore.save).toHaveBeenCalledWith(
        expect.objectContaining({
          state: expect.objectContaining({
            status: SagaStatus.TimedOut
          })
        })
      );
    });
  });
});