import { describe, it, expect } from 'vitest';
import {
  SagaState,
  SagaStatus,
  SagaDefinition,
  SagaStep,
  SagaContext,
  CompensationStrategy,
  SagaEvent,
  SagaMetadata
} from './types';
import { ICommand, IEventPayload, EventDocument } from '../../core/src';

// Test domain events
interface OrderPlaced extends IEventPayload {
  orderId: string;
  customerId: string;
  items: Array<{ productId: string; quantity: number }>;
  totalAmount: number;
}

interface PaymentProcessed extends IEventPayload {
  orderId: string;
  paymentId: string;
  amount: number;
}

interface InventoryReserved extends IEventPayload {
  orderId: string;
  reservationId: string;
  items: Array<{ productId: string; quantity: number }>;
}

interface OrderShipped extends IEventPayload {
  orderId: string;
  shipmentId: string;
  trackingNumber: string;
}

// Test commands
interface ProcessPayment extends ICommand {
  orderId: string;
  amount: number;
  paymentMethod: string;
}

interface ReserveInventory extends ICommand {
  orderId: string;
  items: Array<{ productId: string; quantity: number }>;
}

interface ShipOrder extends ICommand {
  orderId: string;
  shippingAddress: string;
}

interface CancelPayment extends ICommand {
  paymentId: string;
  reason: string;
}

interface ReleaseInventory extends ICommand {
  reservationId: string;
  reason: string;
}

describe('Saga Types', () => {
  describe('SagaState', () => {
    it('should represent saga state with proper structure', () => {
      const sagaState: SagaState = {
        sagaId: 'order-saga-123',
        sagaType: 'OrderFulfillmentSaga',
        status: SagaStatus.Running,
        currentStep: 1,
        startedAt: new Date(),
        context: {
          orderId: 'order-123',
          customerId: 'customer-456',
          paymentId: null,
          reservationId: null
        },
        completedSteps: ['InitiateOrder'],
        failedStep: null,
        error: null
      };

      expect(sagaState.sagaId).toBe('order-saga-123');
      expect(sagaState.status).toBe(SagaStatus.Running);
      expect(sagaState.context.orderId).toBe('order-123');
    });

    it('should support completed state', () => {
      const completedSaga: SagaState = {
        sagaId: 'order-saga-123',
        sagaType: 'OrderFulfillmentSaga',
        status: SagaStatus.Completed,
        currentStep: 3,
        startedAt: new Date('2024-01-01'),
        completedAt: new Date('2024-01-02'),
        context: {
          orderId: 'order-123',
          result: 'Order fulfilled successfully'
        },
        completedSteps: ['InitiateOrder', 'ProcessPayment', 'ShipOrder'],
        failedStep: null,
        error: null
      };

      expect(completedSaga.status).toBe(SagaStatus.Completed);
      expect(completedSaga.completedAt).toBeDefined();
    });

    it('should support failed state with compensation', () => {
      const failedSaga: SagaState = {
        sagaId: 'order-saga-123',
        sagaType: 'OrderFulfillmentSaga',
        status: SagaStatus.Compensating,
        currentStep: 2,
        startedAt: new Date(),
        context: {
          orderId: 'order-123',
          paymentId: 'payment-456',
          error: 'Insufficient inventory'
        },
        completedSteps: ['InitiateOrder', 'ProcessPayment'],
        failedStep: 'ReserveInventory',
        error: new Error('Insufficient inventory'),
        compensatedSteps: ['ProcessPayment']
      };

      expect(failedSaga.status).toBe(SagaStatus.Compensating);
      expect(failedSaga.failedStep).toBe('ReserveInventory');
      expect(failedSaga.compensatedSteps).toContain('ProcessPayment');
    });
  });

  describe('SagaDefinition', () => {
    it('should define a complete saga workflow', () => {
      const orderSaga: SagaDefinition<any> = {
        name: 'OrderFulfillmentSaga',
        version: 1,
        trigger: {
          eventType: 'OrderPlaced',
          filter: (event) => event.payload.totalAmount > 0
        },
        steps: [
          {
            name: 'ProcessPayment',
            command: (context) => ({
              type: 'ProcessPayment',
              payload: {
                orderId: context.orderId,
                amount: context.totalAmount,
                paymentMethod: 'credit_card'
              }
            }),
            compensation: (context) => ({
              type: 'CancelPayment',
              payload: {
                paymentId: context.paymentId,
                reason: 'Order fulfillment failed'
              }
            }),
            onSuccess: (context, event) => ({
              ...context,
              paymentId: event.payload.paymentId
            }),
            retryPolicy: {
              maxAttempts: 3,
              backoffMs: 1000,
              exponential: true
            }
          },
          {
            name: 'ReserveInventory',
            command: (context) => ({
              type: 'ReserveInventory',
              payload: {
                orderId: context.orderId,
                items: context.items
              }
            }),
            compensation: (context) => ({
              type: 'ReleaseInventory',
              payload: {
                reservationId: context.reservationId,
                reason: 'Order fulfillment failed'
              }
            }),
            onSuccess: (context, event) => ({
              ...context,
              reservationId: event.payload.reservationId
            })
          }
        ],
        compensationStrategy: CompensationStrategy.Backward,
        timeout: 300000, // 5 minutes
        metadata: {
          description: 'Handles order fulfillment process',
          tags: ['order', 'payment', 'inventory']
        }
      };

      expect(orderSaga.name).toBe('OrderFulfillmentSaga');
      expect(orderSaga.steps).toHaveLength(2);
      expect(orderSaga.compensationStrategy).toBe(CompensationStrategy.Backward);
      expect(orderSaga.timeout).toBe(300000);
    });
  });

  describe('SagaStep', () => {
    it('should support conditional steps', () => {
      const conditionalStep: SagaStep<any> = {
        name: 'NotifyCustomer',
        condition: (context) => context.totalAmount > 1000,
        command: (context) => ({
          type: 'SendHighValueOrderNotification',
          payload: { orderId: context.orderId }
        }),
        onSuccess: (context) => context,
        optional: true
      };

      expect(conditionalStep.condition).toBeDefined();
      expect(conditionalStep.optional).toBe(true);
    });

    it('should support parallel steps', () => {
      const parallelSteps: SagaStep<any> = {
        name: 'ParallelProcessing',
        parallel: [
          {
            name: 'UpdateInventory',
            command: (context) => ({
              type: 'UpdateInventory',
              payload: { orderId: context.orderId }
            }),
            onSuccess: (context) => context
          },
          {
            name: 'SendEmail',
            command: (context) => ({
              type: 'SendOrderConfirmation',
              payload: { orderId: context.orderId }
            }),
            onSuccess: (context) => context
          }
        ],
        onSuccess: (context) => context
      };

      expect(parallelSteps.parallel).toHaveLength(2);
    });
  });

  describe('SagaEvent', () => {
    it('should represent saga lifecycle events', () => {
      const sagaStarted: SagaEvent = {
        sagaId: 'order-saga-123',
        eventType: 'SagaStarted',
        timestamp: new Date(),
        data: {
          sagaType: 'OrderFulfillmentSaga',
          trigger: { eventType: 'OrderPlaced', eventId: 'event-123' }
        }
      };

      expect(sagaStarted.eventType).toBe('SagaStarted');
      expect(sagaStarted.data.trigger.eventType).toBe('OrderPlaced');
    });

    it('should support step completed events', () => {
      const stepCompleted: SagaEvent = {
        sagaId: 'order-saga-123',
        eventType: 'SagaStepCompleted',
        timestamp: new Date(),
        data: {
          stepName: 'ProcessPayment',
          result: { paymentId: 'payment-456' },
          duration: 1500
        }
      };

      expect(stepCompleted.eventType).toBe('SagaStepCompleted');
      expect(stepCompleted.data.duration).toBe(1500);
    });

    it('should support compensation events', () => {
      const compensationStarted: SagaEvent = {
        sagaId: 'order-saga-123',
        eventType: 'SagaCompensationStarted',
        timestamp: new Date(),
        data: {
          failedStep: 'ReserveInventory',
          error: 'Insufficient stock',
          stepsToCompensate: ['ProcessPayment']
        }
      };

      expect(compensationStarted.eventType).toBe('SagaCompensationStarted');
      expect(compensationStarted.data.stepsToCompensate).toContain('ProcessPayment');
    });
  });

  describe('Compensation Strategy', () => {
    it('should support different compensation strategies', () => {
      // Backward compensation (reverse order)
      expect(CompensationStrategy.Backward).toBe('backward');
      
      // Forward compensation (same order)
      expect(CompensationStrategy.Forward).toBe('forward');
      
      // Parallel compensation (all at once)
      expect(CompensationStrategy.Parallel).toBe('parallel');
      
      // Custom compensation (user-defined order)
      expect(CompensationStrategy.Custom).toBe('custom');
    });
  });
});