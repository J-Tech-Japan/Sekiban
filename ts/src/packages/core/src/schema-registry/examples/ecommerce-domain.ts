/**
 * E-commerce Domain Example using Schema-based Approach
 * 
 * This example demonstrates:
 * - Schema-based events, commands, and projectors
 * - Multiple aggregate types (Order, Inventory)
 * - Cross-aggregate queries
 * - Complex business logic with validation
 */

import { z } from 'zod';
import { ok, err } from 'neverthrow';
import { defineEvent, defineCommand, defineProjector } from '../index.js';
import { PartitionKeys } from '../../documents/partition-keys.js';
import { ValidationError } from '../../result/errors.js';
import type { IMultiProjectionQuery } from '../../queries/query.js';
import type { IEvent } from '../../events/event.js';

// ============================================
// Value Objects
// ============================================

const Money = z.object({
  amount: z.number().min(0),
  currency: z.string().length(3)
});

const OrderItem = z.object({
  productId: z.string(),
  productName: z.string(),
  quantity: z.number().int().positive(),
  unitPrice: Money,
  subtotal: Money
});

const Address = z.object({
  street: z.string(),
  city: z.string(),
  state: z.string(),
  zipCode: z.string(),
  country: z.string()
});

// ============================================
// Order Events
// ============================================

export const OrderCreated = defineEvent({
  type: 'OrderCreated',
  schema: z.object({
    orderId: z.string(),
    customerId: z.string(),
    items: z.array(OrderItem),
    shippingAddress: Address,
    totalAmount: Money,
    createdAt: z.string().datetime()
  })
});

export const OrderItemAdded = defineEvent({
  type: 'OrderItemAdded',
  schema: z.object({
    orderId: z.string(),
    item: OrderItem,
    newTotalAmount: Money
  })
});

export const OrderShipped = defineEvent({
  type: 'OrderShipped',
  schema: z.object({
    orderId: z.string(),
    trackingNumber: z.string(),
    carrier: z.string(),
    shippedAt: z.string().datetime()
  })
});

export const OrderDelivered = defineEvent({
  type: 'OrderDelivered',
  schema: z.object({
    orderId: z.string(),
    deliveredAt: z.string().datetime(),
    signedBy: z.string().optional()
  })
});

export const OrderCancelled = defineEvent({
  type: 'OrderCancelled',
  schema: z.object({
    orderId: z.string(),
    reason: z.string(),
    cancelledAt: z.string().datetime()
  })
});

// ============================================
// Inventory Events
// ============================================

export const InventoryItemAdded = defineEvent({
  type: 'InventoryItemAdded',
  schema: z.object({
    productId: z.string(),
    productName: z.string(),
    sku: z.string(),
    quantity: z.number().int().nonnegative(),
    reorderLevel: z.number().int().nonnegative(),
    unitCost: Money
  })
});

export const InventoryRestocked = defineEvent({
  type: 'InventoryRestocked',
  schema: z.object({
    productId: z.string(),
    quantity: z.number().int().positive(),
    newQuantity: z.number().int().nonnegative(),
    restockedAt: z.string().datetime()
  })
});

export const InventoryReserved = defineEvent({
  type: 'InventoryReserved',
  schema: z.object({
    productId: z.string(),
    orderId: z.string(),
    quantity: z.number().int().positive(),
    newQuantity: z.number().int().nonnegative()
  })
});

export const InventoryReleased = defineEvent({
  type: 'InventoryReleased',
  schema: z.object({
    productId: z.string(),
    orderId: z.string(),
    quantity: z.number().int().positive(),
    newQuantity: z.number().int().nonnegative()
  })
});

// ============================================
// Order Commands
// ============================================

export const CreateOrder = defineCommand({
  type: 'CreateOrder',
  schema: z.object({
    customerId: z.string(),
    items: z.array(z.object({
      productId: z.string(),
      productName: z.string(),
      quantity: z.number().int().positive(),
      unitPrice: Money
    })),
    shippingAddress: Address
  }),
  handlers: {
    specifyPartitionKeys: () => PartitionKeys.generate('Order'),
    
    validate: (data) => {
      // Business validation
      if (data.items.length === 0) {
        return err(new ValidationError('Order must have at least one item'));
      }
      
      // Check for duplicate products
      const productIds = data.items.map(i => i.productId);
      if (new Set(productIds).size !== productIds.length) {
        return err(new ValidationError('Order cannot have duplicate products'));
      }
      
      return ok(undefined);
    },
    
    handle: (data, aggregate) => {
      const orderId = aggregate.partitionKeys.aggregateId;
      
      // Calculate totals
      const items = data.items.map(item => ({
        ...item,
        subtotal: {
          amount: item.quantity * item.unitPrice.amount,
          currency: item.unitPrice.currency
        }
      }));
      
      const totalAmount = items.reduce(
        (total, item) => ({
          amount: total.amount + item.subtotal.amount,
          currency: item.subtotal.currency
        }),
        { amount: 0, currency: items[0].unitPrice.currency }
      );
      
      const event = OrderCreated.create({
        orderId,
        customerId: data.customerId,
        items,
        shippingAddress: data.shippingAddress,
        totalAmount,
        createdAt: new Date().toISOString()
      });
      
      return ok([event]);
    }
  }
});

export const ShipOrder = defineCommand({
  type: 'ShipOrder',
  schema: z.object({
    orderId: z.string(),
    trackingNumber: z.string(),
    carrier: z.string()
  }),
  handlers: {
    specifyPartitionKeys: (data) => PartitionKeys.existing(data.orderId, 'Order'),
    
    validate: (data) => ok(undefined),
    
    handle: (data, aggregate) => {
      // Check if order can be shipped
      const orderState = aggregate.payload as any;
      if (orderState.status !== 'confirmed') {
        return err(new ValidationError('Only confirmed orders can be shipped'));
      }
      
      const event = OrderShipped.create({
        orderId: data.orderId,
        trackingNumber: data.trackingNumber,
        carrier: data.carrier,
        shippedAt: new Date().toISOString()
      });
      
      return ok([event]);
    }
  }
});

// ============================================
// Inventory Commands
// ============================================

export const AddInventoryItem = defineCommand({
  type: 'AddInventoryItem',
  schema: z.object({
    productName: z.string(),
    sku: z.string(),
    initialQuantity: z.number().int().nonnegative(),
    reorderLevel: z.number().int().nonnegative(),
    unitCost: Money
  }),
  handlers: {
    specifyPartitionKeys: () => PartitionKeys.generate('Inventory'),
    
    validate: (data) => ok(undefined),
    
    handle: (data, aggregate) => {
      const productId = aggregate.partitionKeys.aggregateId;
      
      const event = InventoryItemAdded.create({
        productId,
        productName: data.productName,
        sku: data.sku,
        quantity: data.initialQuantity,
        reorderLevel: data.reorderLevel,
        unitCost: data.unitCost
      });
      
      return ok([event]);
    }
  }
});

export const RestockInventory = defineCommand({
  type: 'RestockInventory',
  schema: z.object({
    productId: z.string(),
    quantity: z.number().int().positive()
  }),
  handlers: {
    specifyPartitionKeys: (data) => PartitionKeys.existing(data.productId, 'Inventory'),
    
    validate: (data) => ok(undefined),
    
    handle: (data, aggregate) => {
      const currentState = aggregate.payload as any;
      const newQuantity = (currentState.quantity || 0) + data.quantity;
      
      const event = InventoryRestocked.create({
        productId: data.productId,
        quantity: data.quantity,
        newQuantity,
        restockedAt: new Date().toISOString()
      });
      
      return ok([event]);
    }
  }
});

// ============================================
// Projectors
// ============================================

export const OrderProjector = defineProjector({
  aggregateType: 'Order',
  initialState: () => ({ aggregateType: 'Empty' as const }),
  projections: {
    OrderCreated: (state, event) => ({
      aggregateType: 'Order' as const,
      orderId: event.orderId,
      customerId: event.customerId,
      items: event.items,
      shippingAddress: event.shippingAddress,
      totalAmount: event.totalAmount,
      status: 'confirmed' as const,
      createdAt: event.createdAt
    }),
    
    OrderItemAdded: (state, event) => ({
      ...state,
      aggregateType: 'Order' as const,
      items: [...(state as any).items, event.item],
      totalAmount: event.newTotalAmount
    }),
    
    OrderShipped: (state, event) => ({
      ...state,
      aggregateType: 'Order' as const,
      status: 'shipped' as const,
      trackingNumber: event.trackingNumber,
      carrier: event.carrier,
      shippedAt: event.shippedAt
    }),
    
    OrderDelivered: (state, event) => ({
      ...state,
      aggregateType: 'Order' as const,
      status: 'delivered' as const,
      deliveredAt: event.deliveredAt,
      signedBy: event.signedBy
    }),
    
    OrderCancelled: (state, event) => ({
      ...state,
      aggregateType: 'Order' as const,
      status: 'cancelled' as const,
      cancelReason: event.reason,
      cancelledAt: event.cancelledAt
    })
  }
});

export const InventoryProjector = defineProjector({
  aggregateType: 'Inventory',
  initialState: () => ({ aggregateType: 'Empty' as const }),
  projections: {
    InventoryItemAdded: (state, event) => ({
      aggregateType: 'Inventory' as const,
      productId: event.productId,
      productName: event.productName,
      sku: event.sku,
      quantity: event.quantity,
      reorderLevel: event.reorderLevel,
      unitCost: event.unitCost,
      reserved: {} as Record<string, number>
    }),
    
    InventoryRestocked: (state, event) => ({
      ...state,
      aggregateType: 'Inventory' as const,
      quantity: event.newQuantity
    }),
    
    InventoryReserved: (state, event) => ({
      ...state,
      aggregateType: 'Inventory' as const,
      quantity: event.newQuantity,
      reserved: {
        ...(state as any).reserved,
        [event.orderId]: event.quantity
      }
    }),
    
    InventoryReleased: (state, event) => {
      const reserved = { ...(state as any).reserved };
      delete reserved[event.orderId];
      
      return {
        ...state,
        aggregateType: 'Inventory' as const,
        quantity: event.newQuantity,
        reserved
      };
    }
  }
});

// ============================================
// Multi-Projection Queries
// ============================================

export class LowStockQuery implements IMultiProjectionQuery<any, any, Array<{
  productId: string;
  productName: string;
  currentQuantity: number;
  reorderLevel: number;
}>> {
  query = async (events: IEvent[]): Promise<Array<{
    productId: string;
    productName: string;
    currentQuantity: number;
    reorderLevel: number;
  }>> => {
    // Build inventory states from events
    const inventoryStates = new Map<string, any>();
    
    for (const event of events) {
      if (event.aggregateType !== 'Inventory') continue;
      
      const productId = event.partitionKeys.aggregateId;
      const currentState = inventoryStates.get(productId) || { aggregateType: 'Empty' as const };
      
      // Apply event to state
      let newState = currentState;
      switch (event.eventType) {
        case 'InventoryItemAdded':
          newState = {
            aggregateType: 'Inventory' as const,
            productId: event.payload.productId,
            productName: event.payload.productName,
            sku: event.payload.sku,
            quantity: event.payload.quantity,
            reorderLevel: event.payload.reorderLevel,
            unitCost: event.payload.unitCost,
            reserved: {}
          };
          break;
        case 'InventoryRestocked':
          newState = {
            ...currentState,
            aggregateType: 'Inventory' as const,
            quantity: event.payload.newQuantity
          };
          break;
        case 'InventoryReserved':
          newState = {
            ...currentState,
            aggregateType: 'Inventory' as const,
            quantity: event.payload.newQuantity,
            reserved: {
              ...(currentState.reserved || {}),
              [event.payload.orderId]: event.payload.quantity
            }
          };
          break;
        case 'InventoryReleased':
          const reserved = { ...(currentState.reserved || {}) };
          delete reserved[event.payload.orderId];
          newState = {
            ...currentState,
            aggregateType: 'Inventory' as const,
            quantity: event.payload.newQuantity,
            reserved
          };
          break;
      }
      
      if (newState !== currentState) {
        inventoryStates.set(productId, newState);
      }
    }
    
    // Find items below reorder level
    const lowStockItems: Array<{
      productId: string;
      productName: string;
      currentQuantity: number;
      reorderLevel: number;
    }> = [];
    
    for (const [productId, state] of inventoryStates) {
      if (state.aggregateType === 'Inventory' && state.quantity <= state.reorderLevel) {
        lowStockItems.push({
          productId,
          productName: state.productName,
          currentQuantity: state.quantity,
          reorderLevel: state.reorderLevel
        });
      }
    }
    
    return lowStockItems;
  }
}

export class OrdersByCustomerQuery implements IMultiProjectionQuery<any, any, Array<{
  orderId: string;
  customerId: string;
  totalAmount: Money;
  status: string;
  createdAt: string;
}>> {
  constructor(private customerId: string) {}
  
  query = async (events: IEvent[]): Promise<Array<{
    orderId: string;
    customerId: string;
    totalAmount: Money;
    status: string;
    createdAt: string;
  }>> => {
    const orderStates = new Map<string, any>();
    
    for (const event of events) {
      if (event.aggregateType !== 'Order') continue;
      
      const orderId = event.partitionKeys.aggregateId;
      const currentState = orderStates.get(orderId) || { aggregateType: 'Empty' as const };
      
      // Apply event to state
      let newState = currentState;
      switch (event.eventType) {
        case 'OrderCreated':
          newState = {
            aggregateType: 'Order' as const,
            orderId: event.payload.orderId,
            customerId: event.payload.customerId,
            items: event.payload.items,
            shippingAddress: event.payload.shippingAddress,
            totalAmount: event.payload.totalAmount,
            status: 'confirmed' as const,
            createdAt: event.payload.createdAt
          };
          break;
        case 'OrderItemAdded':
          newState = {
            ...currentState,
            aggregateType: 'Order' as const,
            items: [...(currentState.items || []), event.payload.item],
            totalAmount: event.payload.newTotalAmount
          };
          break;
        case 'OrderShipped':
          newState = {
            ...currentState,
            aggregateType: 'Order' as const,
            status: 'shipped' as const,
            trackingNumber: event.payload.trackingNumber,
            carrier: event.payload.carrier,
            shippedAt: event.payload.shippedAt
          };
          break;
        case 'OrderDelivered':
          newState = {
            ...currentState,
            aggregateType: 'Order' as const,
            status: 'delivered' as const,
            deliveredAt: event.payload.deliveredAt,
            signedBy: event.payload.signedBy
          };
          break;
        case 'OrderCancelled':
          newState = {
            ...currentState,
            aggregateType: 'Order' as const,
            status: 'cancelled' as const,
            cancelReason: event.payload.reason,
            cancelledAt: event.payload.cancelledAt
          };
          break;
      }
      
      if (newState !== currentState) {
        orderStates.set(orderId, newState);
      }
    }
    
    // Filter orders by customer
    const customerOrders: Array<{
      orderId: string;
      customerId: string;
      totalAmount: Money;
      status: string;
      createdAt: string;
    }> = [];
    
    for (const [orderId, state] of orderStates) {
      if (state.aggregateType === 'Order' && state.customerId === this.customerId) {
        customerOrders.push({
          orderId,
          customerId: state.customerId,
          totalAmount: state.totalAmount,
          status: state.status,
          createdAt: state.createdAt
        });
      }
    }
    
    // Sort by creation date
    return customerOrders.sort((a, b) => 
      new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime()
    );
  }
}