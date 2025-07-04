import { describe, it, expect, vi, beforeEach } from 'vitest';
import { SagaCoordinator } from './saga-coordinator';
import { ChoreographySaga, SagaReaction, ReactionCondition } from './choreography-types';
import { 
  IEventPayload, 
  EventDocument,
  PartitionKeys,
  SortableUniqueId
} from '../../core/src';
import { ok, err } from '../../core/src/result';

// Test events
interface OrderPlaced extends IEventPayload {
  orderId: string;
  customerId: string;
  totalAmount: number;
}

interface PaymentRequested extends IEventPayload {
  orderId: string;
  amount: number;
}

interface PaymentCompleted extends IEventPayload {
  orderId: string;
  paymentId: string;
}

interface PaymentFailed extends IEventPayload {
  orderId: string;
  reason: string;
}

interface InventoryReserved extends IEventPayload {
  orderId: string;
  reservationId: string;
}

interface OrderCancelled extends IEventPayload {
  orderId: string;
  reason: string;
}

describe('SagaCoordinator', () => {
  let coordinator: SagaCoordinator;
  let mockEventStore: any;
  let mockCommandExecutor: any;
  
  const createEvent = <T extends IEventPayload>(
    eventType: string,
    payload: T
  ): EventDocument<T> => ({
    id: `event-${Date.now()}`,
    eventType,
    payload,
    version: 1,
    timestamp: new Date(),
    sortableUniqueId: SortableUniqueId.generate(),
    partitionKeys: PartitionKeys.create('test', 'test')
  });

  const orderChoreography: ChoreographySaga = {
    name: 'OrderProcessingChoreography',
    version: 1,
    reactions: [
      {
        name: 'RequestPaymentOnOrderPlaced',
        trigger: {
          eventType: 'OrderPlaced',
          condition: (event) => (event.payload as OrderPlaced).totalAmount > 0
        },
        action: {
          type: 'command',
          command: (event) => ({
            type: 'RequestPayment',
            payload: {
              orderId: (event.payload as OrderPlaced).orderId,
              amount: (event.payload as OrderPlaced).totalAmount
            }
          })
        }
      },
      {
        name: 'ReserveInventoryOnPaymentCompleted',
        trigger: {
          eventType: 'PaymentCompleted'
        },
        action: {
          type: 'command',
          command: (event) => ({
            type: 'ReserveInventory',
            payload: {
              orderId: (event.payload as PaymentCompleted).orderId
            }
          })
        }
      },
      {
        name: 'CancelOrderOnPaymentFailed',
        trigger: {
          eventType: 'PaymentFailed'
        },
        action: {
          type: 'event',
          event: (trigger) => ({
            eventType: 'OrderCancelled',
            payload: {
              orderId: (trigger.payload as PaymentFailed).orderId,
              reason: `Payment failed: ${(trigger.payload as PaymentFailed).reason}`
            }
          })
        }
      }
    ],
    metadata: {
      description: 'Choreography-based order processing saga'
    }
  };

  beforeEach(() => {
    mockEventStore = {
      append: vi.fn(),
      getEvents: vi.fn()
    };
    
    mockCommandExecutor = {
      execute: vi.fn()
    };
    
    coordinator = new SagaCoordinator({
      eventStore: mockEventStore,
      commandExecutor: mockCommandExecutor
    });
  });

  describe('Choreography Registration', () => {
    it('should register a choreography saga', () => {
      coordinator.register(orderChoreography);
      
      const registered = coordinator.getChoreography('OrderProcessingChoreography');
      expect(registered).toBe(orderChoreography);
    });

    it('should support multiple choreography registrations', () => {
      const anotherChoreography: ChoreographySaga = {
        ...orderChoreography,
        name: 'ShippingChoreography'
      };
      
      coordinator.register(orderChoreography);
      coordinator.register(anotherChoreography);
      
      expect(coordinator.getChoreography('OrderProcessingChoreography')).toBeDefined();
      expect(coordinator.getChoreography('ShippingChoreography')).toBeDefined();
    });
  });

  describe('Event Reactions', () => {
    it('should trigger command reaction on matching event', async () => {
      coordinator.register(orderChoreography);
      
      const orderPlacedEvent = createEvent<OrderPlaced>('OrderPlaced', {
        orderId: 'order-123',
        customerId: 'customer-456',
        totalAmount: 100
      });

      mockCommandExecutor.execute.mockResolvedValue(ok([]));
      
      const result = await coordinator.handleEvent(orderPlacedEvent);
      
      expect(result.isOk()).toBe(true);
      expect(mockCommandExecutor.execute).toHaveBeenCalledWith({
        type: 'RequestPayment',
        payload: {
          orderId: 'order-123',
          amount: 100
        }
      });
    });

    it('should trigger event reaction on matching event', async () => {
      coordinator.register(orderChoreography);
      
      const paymentFailedEvent = createEvent<PaymentFailed>('PaymentFailed', {
        orderId: 'order-123',
        reason: 'Insufficient funds'
      });

      mockEventStore.append.mockResolvedValue(ok(undefined));
      
      const result = await coordinator.handleEvent(paymentFailedEvent);
      
      expect(result.isOk()).toBe(true);
      expect(mockEventStore.append).toHaveBeenCalledWith(
        expect.objectContaining({
          eventType: 'OrderCancelled',
          payload: {
            orderId: 'order-123',
            reason: 'Payment failed: Insufficient funds'
          }
        })
      );
    });

    it('should respect reaction conditions', async () => {
      const conditionalChoreography: ChoreographySaga = {
        name: 'ConditionalChoreography',
        version: 1,
        reactions: [
          {
            name: 'HighValueOrderProcessing',
            trigger: {
              eventType: 'OrderPlaced',
              condition: (event) => (event.payload as OrderPlaced).totalAmount > 1000
            },
            action: {
              type: 'command',
              command: () => ({ type: 'ProcessHighValueOrder', payload: {} })
            }
          }
        ]
      };
      
      coordinator.register(conditionalChoreography);
      
      const lowValueOrder = createEvent<OrderPlaced>('OrderPlaced', {
        orderId: 'order-123',
        customerId: 'customer-456',
        totalAmount: 50 // Below threshold
      });
      
      const result = await coordinator.handleEvent(lowValueOrder);
      
      expect(result.isOk()).toBe(true);
      expect(mockCommandExecutor.execute).not.toHaveBeenCalled();
    });
  });

  describe('Complex Reactions', () => {
    it('should support correlated reactions', async () => {
      const correlatedChoreography: ChoreographySaga = {
        name: 'CorrelatedChoreography',
        version: 1,
        reactions: [
          {
            name: 'CompleteOrderOnInventoryReserved',
            trigger: {
              eventType: 'InventoryReserved',
              correlation: {
                key: 'orderId',
                requires: ['PaymentCompleted'],
                within: 300000 // 5 minutes
              }
            },
            action: {
              type: 'command',
              command: (event) => ({
                type: 'CompleteOrder',
                payload: {
                  orderId: (event.payload as InventoryReserved).orderId
                }
              })
            }
          }
        ]
      };
      
      coordinator.register(correlatedChoreography);
      
      // Mock that PaymentCompleted exists for this order
      mockEventStore.getEvents.mockResolvedValue(ok([
        createEvent<PaymentCompleted>('PaymentCompleted', {
          orderId: 'order-123',
          paymentId: 'payment-456'
        })
      ]));
      
      mockCommandExecutor.execute.mockResolvedValue(ok([]));
      
      const inventoryReservedEvent = createEvent<InventoryReserved>('InventoryReserved', {
        orderId: 'order-123',
        reservationId: 'reservation-789'
      });
      
      const result = await coordinator.handleEvent(inventoryReservedEvent);
      
      expect(result.isOk()).toBe(true);
      expect(mockEventStore.getEvents).toHaveBeenCalledWith(
        expect.objectContaining({
          correlationKey: 'orderId',
          correlationValue: 'order-123'
        })
      );
      expect(mockCommandExecutor.execute).toHaveBeenCalledWith({
        type: 'CompleteOrder',
        payload: { orderId: 'order-123' }
      });
    });

    it('should handle timeout reactions', async () => {
      vi.useFakeTimers();
      
      const timeoutChoreography: ChoreographySaga = {
        name: 'TimeoutChoreography',
        version: 1,
        reactions: [
          {
            name: 'TimeoutOrderIfNoPayment',
            trigger: {
              eventType: 'OrderPlaced',
              timeout: {
                duration: 60000, // 1 minute
                action: {
                  type: 'event',
                  event: (trigger) => ({
                    eventType: 'OrderTimedOut',
                    payload: {
                      orderId: (trigger.payload as OrderPlaced).orderId,
                      reason: 'Payment timeout'
                    }
                  })
                },
                unless: ['PaymentCompleted', 'OrderCancelled']
              }
            },
            action: {
              type: 'command',
              command: () => ({ type: 'StartPaymentTimer', payload: {} })
            }
          }
        ]
      };
      
      coordinator.register(timeoutChoreography);
      
      mockCommandExecutor.execute.mockResolvedValue(ok([]));
      mockEventStore.append.mockResolvedValue(ok(undefined));
      mockEventStore.getEvents.mockResolvedValue(ok([])); // No payment events
      
      const orderPlacedEvent = createEvent<OrderPlaced>('OrderPlaced', {
        orderId: 'order-123',
        customerId: 'customer-456',
        totalAmount: 100
      });
      
      await coordinator.handleEvent(orderPlacedEvent);
      
      // Fast forward time
      vi.advanceTimersByTime(61000);
      
      // Check timeout should be processed
      await coordinator.processTimeouts();
      
      expect(mockEventStore.append).toHaveBeenCalledWith(
        expect.objectContaining({
          eventType: 'OrderTimedOut',
          payload: {
            orderId: 'order-123',
            reason: 'Payment timeout'
          }
        })
      );
      
      vi.useRealTimers();
    });
  });

  describe('Policy-based Reactions', () => {
    it('should support policy-based reactions', async () => {
      const policyChoreography: ChoreographySaga = {
        name: 'PolicyChoreography',
        version: 1,
        reactions: [
          {
            name: 'RetryPaymentPolicy',
            trigger: {
              eventType: 'PaymentFailed',
              policy: {
                maxOccurrences: 3,
                window: 3600000 // 1 hour
              }
            },
            action: {
              type: 'command',
              command: (event) => ({
                type: 'RetryPayment',
                payload: {
                  orderId: (event.payload as PaymentFailed).orderId
                }
              })
            }
          }
        ]
      };
      
      coordinator.register(policyChoreography);
      
      // Mock that this is the first failure
      mockEventStore.getEvents.mockResolvedValue(ok([]));
      mockCommandExecutor.execute.mockResolvedValue(ok([]));
      
      const paymentFailedEvent = createEvent<PaymentFailed>('PaymentFailed', {
        orderId: 'order-123',
        reason: 'Network error'
      });
      
      const result = await coordinator.handleEvent(paymentFailedEvent);
      
      expect(result.isOk()).toBe(true);
      expect(mockCommandExecutor.execute).toHaveBeenCalledWith({
        type: 'RetryPayment',
        payload: { orderId: 'order-123' }
      });
    });

    it('should not trigger reaction when policy limit exceeded', async () => {
      const policyChoreography: ChoreographySaga = {
        name: 'PolicyChoreography',
        version: 1,
        reactions: [
          {
            name: 'RetryPaymentPolicy',
            trigger: {
              eventType: 'PaymentFailed',
              policy: {
                maxOccurrences: 2,
                window: 3600000
              }
            },
            action: {
              type: 'command',
              command: () => ({ type: 'RetryPayment', payload: {} })
            }
          }
        ]
      };
      
      coordinator.register(policyChoreography);
      mockCommandExecutor.execute.mockResolvedValue(ok([]));
      
      // Trigger the reaction twice to reach the limit
      const paymentFailedEvent1 = createEvent<PaymentFailed>('PaymentFailed', {
        orderId: 'order-123',
        reason: 'Error 1'
      });
      await coordinator.handleEvent(paymentFailedEvent1);
      
      const paymentFailedEvent2 = createEvent<PaymentFailed>('PaymentFailed', {
        orderId: 'order-123',
        reason: 'Error 2'
      });
      await coordinator.handleEvent(paymentFailedEvent2);
      
      // Reset the mock to check the third attempt
      mockCommandExecutor.execute.mockClear();
      
      // Third attempt should not trigger due to policy limit
      const paymentFailedEvent3 = createEvent<PaymentFailed>('PaymentFailed', {
        orderId: 'order-123',
        reason: 'Error 3'
      });
      
      const result = await coordinator.handleEvent(paymentFailedEvent3);
      
      expect(result.isOk()).toBe(true);
      expect(mockCommandExecutor.execute).not.toHaveBeenCalled();
    });
  });

  describe('Reaction Chains', () => {
    it('should support chained reactions', async () => {
      const chainedChoreography: ChoreographySaga = {
        name: 'ChainedChoreography',
        version: 1,
        reactions: [
          {
            name: 'StartChain',
            trigger: { eventType: 'ChainStarted' },
            action: {
              type: 'event',
              event: () => ({
                eventType: 'Step1Completed',
                payload: {}
              })
            },
            chain: [
              {
                name: 'Step2',
                trigger: { eventType: 'Step1Completed' },
                action: {
                  type: 'event',
                  event: () => ({
                    eventType: 'Step2Completed',
                    payload: {}
                  })
                }
              },
              {
                name: 'Step3',
                trigger: { eventType: 'Step2Completed' },
                action: {
                  type: 'event',
                  event: () => ({
                    eventType: 'ChainCompleted',
                    payload: {}
                  })
                }
              }
            ]
          }
        ]
      };
      
      coordinator.register(chainedChoreography);
      
      mockEventStore.append.mockResolvedValue(ok(undefined));
      
      const startEvent = createEvent('ChainStarted', {});
      
      const result = await coordinator.handleEvent(startEvent);
      
      expect(result.isOk()).toBe(true);
      // Should trigger the chain of events
      expect(mockEventStore.append).toHaveBeenCalledTimes(1);
      expect(mockEventStore.append).toHaveBeenCalledWith(
        expect.objectContaining({
          eventType: 'Step1Completed'
        })
      );
    });
  });
});