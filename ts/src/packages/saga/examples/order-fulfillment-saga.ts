/**
 * Example: Order Fulfillment Saga with Persistence
 * 
 * This example demonstrates how to create a complete saga that manages
 * the order fulfillment process with proper persistence and error handling.
 */

import { 
  SagaManager,
  SagaCoordinator,
  SagaDefinition,
  ChoreographySaga,
  CompensationStrategy,
  SagaStatus,
  createSagaStore,
  InMemorySagaRepository,
  JsonFileSagaRepository,
  SagaSnapshotUtils
} from '../src';
import { ICommand, IEventPayload, EventDocument, PartitionKeys, SortableUniqueId } from '../../core/src';

// Domain Events
interface OrderPlaced extends IEventPayload {
  orderId: string;
  customerId: string;
  items: Array<{ productId: string; quantity: number; price: number }>;
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

interface ShippingScheduled extends IEventPayload {
  orderId: string;
  trackingId: string;
  estimatedDelivery: Date;
}

interface OrderCompleted extends IEventPayload {
  orderId: string;
  completedAt: Date;
}

// Commands
interface ProcessPayment extends ICommand {
  orderId: string;
  customerId: string;
  amount: number;
}

interface ReserveInventory extends ICommand {
  orderId: string;
  items: Array<{ productId: string; quantity: number }>;
}

interface ScheduleShipping extends ICommand {
  orderId: string;
  customerId: string;
  items: Array<{ productId: string; quantity: number }>;
}

// Saga Context
interface OrderFulfillmentContext {
  orderId: string;
  customerId: string;
  totalAmount: number;
  items: Array<{ productId: string; quantity: number; price: number }>;
  
  // Step results
  paymentId?: string;
  reservationId?: string;
  trackingId?: string;
  
  // State tracking
  currentStep: 'payment' | 'inventory' | 'shipping' | 'completed';
  startedAt: Date;
  completedAt?: Date;
}

/**
 * Orchestration-style Order Fulfillment Saga
 */
export const OrderFulfillmentSaga: SagaDefinition<OrderFulfillmentContext> = {
  name: 'OrderFulfillmentSaga',
  version: 1,
  trigger: {
    eventType: 'OrderPlaced',
    filter: (event) => {
      const payload = event.payload as OrderPlaced;
      return payload.totalAmount > 0 && payload.items.length > 0;
    }
  },
  initialContext: (triggerEvent) => {
    const payload = triggerEvent.payload as OrderPlaced;
    return {
      orderId: payload.orderId,
      customerId: payload.customerId,
      totalAmount: payload.totalAmount,
      items: payload.items,
      currentStep: 'payment' as const,
      startedAt: new Date()
    };
  },
  steps: [
    {
      name: 'ProcessPayment',
      command: (context) => ({
        type: 'ProcessPayment',
        payload: {
          orderId: context.orderId,
          customerId: context.customerId,
          amount: context.totalAmount
        }
      } as ProcessPayment & { type: string }),
      onSuccess: (context, event) => {
        const payload = event?.payload as PaymentProcessed;
        return {
          ...context,
          paymentId: payload.paymentId,
          currentStep: 'inventory' as const
        };
      },
      compensation: (context) => ({
        type: 'RefundPayment',
        payload: {
          paymentId: context.paymentId!,
          amount: context.totalAmount,
          reason: 'Order fulfillment failed'
        }
      } as ICommand & { type: string }),
      retryPolicy: {
        maxAttempts: 3,
        backoffMs: 1000,
        exponential: true,
        maxBackoffMs: 10000
      }
    },
    {
      name: 'ReserveInventory',
      condition: (context) => !!context.paymentId,
      command: (context) => ({
        type: 'ReserveInventory',
        payload: {
          orderId: context.orderId,
          items: context.items.map(item => ({
            productId: item.productId,
            quantity: item.quantity
          }))
        }
      } as ReserveInventory & { type: string }),
      onSuccess: (context, event) => {
        const payload = event?.payload as InventoryReserved;
        return {
          ...context,
          reservationId: payload.reservationId,
          currentStep: 'shipping' as const
        };
      },
      compensation: (context) => ({
        type: 'ReleaseInventory',
        payload: {
          reservationId: context.reservationId!,
          orderId: context.orderId
        }
      } as ICommand & { type: string }),
      retryPolicy: {
        maxAttempts: 2,
        backoffMs: 500
      }
    },
    {
      name: 'ScheduleShipping',
      condition: (context) => !!context.reservationId,
      command: (context) => ({
        type: 'ScheduleShipping',
        payload: {
          orderId: context.orderId,
          customerId: context.customerId,
          items: context.items.map(item => ({
            productId: item.productId,
            quantity: item.quantity
          }))
        }
      } as ScheduleShipping & { type: string }),
      onSuccess: (context, event) => {
        const payload = event?.payload as ShippingScheduled;
        return {
          ...context,
          trackingId: payload.trackingId,
          currentStep: 'completed' as const,
          completedAt: new Date()
        };
      },
      compensation: (context) => ({
        type: 'CancelShipping',
        payload: {
          trackingId: context.trackingId!,
          orderId: context.orderId
        }
      } as ICommand & { type: string })
    }
  ],
  compensationStrategy: CompensationStrategy.Backward,
  timeout: 1800000, // 30 minutes
  onComplete: (context) => {
    console.log(`Order ${context.orderId} fulfillment completed successfully`);
  },
  onCompensated: (context) => {
    console.log(`Order ${context.orderId} fulfillment was compensated`);
  },
  onTimeout: (context) => {
    console.log(`Order ${context.orderId} fulfillment timed out`);
  }
};

/**
 * Choreography-style Order Processing
 */
export const OrderProcessingChoreography: ChoreographySaga = {
  name: 'OrderProcessingChoreography',
  version: 1,
  reactions: [
    {
      name: 'InitiatePaymentOnOrderPlaced',
      trigger: {
        eventType: 'OrderPlaced',
        condition: (event) => (event.payload as OrderPlaced).totalAmount > 0
      },
      action: {
        type: 'command',
        command: (event) => {
          const payload = event.payload as OrderPlaced;
          return {
            type: 'ProcessPayment',
            payload: {
              orderId: payload.orderId,
              customerId: payload.customerId,
              amount: payload.totalAmount
            }
          };
        }
      }
    },
    {
      name: 'ReserveInventoryOnPaymentSuccess',
      trigger: {
        eventType: 'PaymentProcessed',
        correlation: {
          key: 'orderId',
          requires: ['OrderPlaced'],
          within: 300000 // 5 minutes
        }
      },
      action: {
        type: 'command',
        command: (event) => {
          const payload = event.payload as PaymentProcessed;
          return {
            type: 'ReserveInventory',
            payload: {
              orderId: payload.orderId,
              // Note: In real implementation, you'd need to correlate with OrderPlaced to get items
              items: []
            }
          };
        }
      }
    },
    {
      name: 'ScheduleShippingOnInventoryReserved',
      trigger: {
        eventType: 'InventoryReserved',
        correlation: {
          key: 'orderId',
          requires: ['PaymentProcessed'],
          within: 600000 // 10 minutes
        }
      },
      action: {
        type: 'event',
        event: (trigger) => {
          const payload = trigger.payload as InventoryReserved;
          return {
            eventType: 'ShippingScheduled',
            payload: {
              orderId: payload.orderId,
              trackingId: `TRACK-${Date.now()}`,
              estimatedDelivery: new Date(Date.now() + 7 * 24 * 60 * 60 * 1000) // 7 days
            }
          };
        }
      }
    },
    {
      name: 'CompleteOrderOnShippingScheduled',
      trigger: {
        eventType: 'ShippingScheduled',
        correlation: {
          key: 'orderId',
          requires: ['PaymentProcessed', 'InventoryReserved'],
          within: 900000 // 15 minutes
        }
      },
      action: {
        type: 'event',
        event: (trigger) => {
          const payload = trigger.payload as ShippingScheduled;
          return {
            eventType: 'OrderCompleted',
            payload: {
              orderId: payload.orderId,
              completedAt: new Date()
            }
          };
        }
      }
    }
  ],
  metadata: {
    description: 'Choreography-based order processing workflow',
    tags: ['order', 'payment', 'inventory', 'shipping']
  }
};

/**
 * Complete example setup with persistence
 */
export class OrderFulfillmentSystem {
  private sagaManager: SagaManager;
  private sagaCoordinator: SagaCoordinator;
  private repository: InMemorySagaRepository | JsonFileSagaRepository;

  constructor(options: {
    persistenceType: 'memory' | 'file';
    dataDirectory?: string;
  }) {
    // Setup repository
    this.repository = options.persistenceType === 'memory' 
      ? new InMemorySagaRepository()
      : new JsonFileSagaRepository({
          dataDirectory: options.dataDirectory || './saga-data',
          prettyPrint: true,
          enableAutoCleanup: true,
          cleanupIntervalMs: 60000 // 1 minute
        });

    // Setup saga manager with persistence
    const sagaStore = createSagaStore<OrderFulfillmentContext>(this.repository);
    this.sagaManager = new SagaManager({
      commandExecutor: this.createMockCommandExecutor(),
      sagaStore
    });

    // Setup saga coordinator
    this.sagaCoordinator = new SagaCoordinator({
      eventStore: this.createMockEventStore(),
      commandExecutor: this.createMockCommandExecutor()
    });

    // Register sagas
    this.sagaManager.register(OrderFulfillmentSaga);
    this.sagaCoordinator.register(OrderProcessingChoreography);
  }

  /**
   * Process an order placement event
   */
  async processOrderPlaced(order: OrderPlaced): Promise<void> {
    const event = this.createTestEvent('OrderPlaced', order);
    
    // Handle with orchestration saga
    await this.sagaManager.handleEvent(event);
    
    // Handle with choreography saga
    await this.sagaCoordinator.handleEvent(event);
  }

  /**
   * Execute next step for a specific saga
   */
  async continueOrderProcessing(sagaId: string): Promise<void> {
    await this.sagaManager.executeNextStep(sagaId);
  }

  /**
   * Get order status from saga
   */
  async getOrderStatus(orderId: string): Promise<{
    status: SagaStatus;
    currentStep: string;
    progress: number;
  } | null> {
    // In a real implementation, you'd have a mapping from orderId to sagaId
    const sagaId = `OrderFulfillmentSaga-${orderId}-*`; // This is a simplification
    
    try {
      const snapshot = await this.repository.load(sagaId);
      if (!snapshot) return null;

      const status = SagaSnapshotUtils.getStatus(snapshot);
      const state = snapshot.state;
      
      // Calculate progress based on current step
      const stepProgress = {
        'payment': 25,
        'inventory': 50,
        'shipping': 75,
        'completed': 100
      };

      return {
        status: state.status || SagaStatus.Running,
        currentStep: state.currentStep || 'payment',
        progress: stepProgress[state.currentStep as keyof typeof stepProgress] || 0
      };
    } catch (error) {
      console.error('Failed to get order status:', error);
      return null;
    }
  }

  /**
   * List all active orders
   */
  async listActiveOrders(): Promise<Array<{
    orderId: string;
    status: string;
    currentStep: string;
    startedAt: Date;
  }>> {
    const runningSnapshots = await this.repository.findByStatus('running');
    
    return runningSnapshots.map(snapshot => ({
      orderId: snapshot.state.orderId || snapshot.id,
      status: SagaSnapshotUtils.getStatus(snapshot),
      currentStep: snapshot.state.currentStep || 'unknown',
      startedAt: snapshot.createdAt
    }));
  }

  /**
   * Cleanup expired sagas
   */
  async cleanup(): Promise<number> {
    if (this.repository instanceof InMemorySagaRepository) {
      return await this.repository.cleanupExpired();
    } else if (this.repository instanceof JsonFileSagaRepository) {
      return await this.repository.cleanupExpired();
    }
    return 0;
  }

  /**
   * Dispose of resources
   */
  dispose(): void {
    if (this.repository instanceof InMemorySagaRepository) {
      this.repository.dispose();
    } else if (this.repository instanceof JsonFileSagaRepository) {
      this.repository.dispose();
    }
  }

  private createMockCommandExecutor() {
    return {
      async execute(command: ICommand & { type: string }) {
        console.log(`Executing command: ${command.type}`, command.payload);
        
        // Simulate command execution
        await new Promise(resolve => setTimeout(resolve, 100));
        
        // Return mock events based on command type
        switch (command.type) {
          case 'ProcessPayment':
            return Promise.resolve(ok([this.createTestEvent('PaymentProcessed', {
              orderId: (command.payload as any).orderId,
              paymentId: `PAY-${Date.now()}`,
              amount: (command.payload as any).amount
            })]));
            
          case 'ReserveInventory':
            return Promise.resolve(ok([this.createTestEvent('InventoryReserved', {
              orderId: (command.payload as any).orderId,
              reservationId: `RES-${Date.now()}`,
              items: (command.payload as any).items
            })]));
            
          case 'ScheduleShipping':
            return Promise.resolve(ok([this.createTestEvent('ShippingScheduled', {
              orderId: (command.payload as any).orderId,
              trackingId: `TRACK-${Date.now()}`,
              estimatedDelivery: new Date(Date.now() + 7 * 24 * 60 * 60 * 1000)
            })]));
            
          default:
            return Promise.resolve(ok([]));
        }
      }
    };
  }

  private createMockEventStore() {
    return {
      async append(event: { eventType: string; payload: IEventPayload }) {
        console.log(`Appending event: ${event.eventType}`, event.payload);
        return Promise.resolve(ok(undefined));
      },
      
      async getEvents(filter: any) {
        console.log('Getting events with filter:', filter);
        return Promise.resolve(ok([]));
      }
    };
  }

  private createTestEvent<T extends IEventPayload>(eventType: string, payload: T): EventDocument<T> {
    return {
      id: `event-${Date.now()}`,
      eventType,
      payload,
      version: 1,
      timestamp: new Date(),
      sortableUniqueId: SortableUniqueId.generate(),
      partitionKeys: PartitionKeys.create(payload.orderId || 'default', 'orders')
    };
  }
}

// Usage example
export async function demonstrateOrderFulfillment() {
  const system = new OrderFulfillmentSystem({
    persistenceType: 'memory' // or 'file'
  });

  try {
    // Process a new order
    const order: OrderPlaced = {
      orderId: 'order-12345',
      customerId: 'customer-abc',
      items: [
        { productId: 'product-1', quantity: 2, price: 50 },
        { productId: 'product-2', quantity: 1, price: 30 }
      ],
      totalAmount: 130
    };

    console.log('Processing order:', order.orderId);
    await system.processOrderPlaced(order);

    // Check order status
    const status = await system.getOrderStatus(order.orderId);
    console.log('Order status:', status);

    // List active orders
    const activeOrders = await system.listActiveOrders();
    console.log('Active orders:', activeOrders);

    // Continue processing (simulate step execution)
    // In a real system, this would be triggered by a scheduler or event
    if (activeOrders.length > 0) {
      await system.continueOrderProcessing(activeOrders[0].orderId);
    }

  } finally {
    system.dispose();
  }
}

// Export for use in other modules
export { ok, err } from '../../../core/src/result';