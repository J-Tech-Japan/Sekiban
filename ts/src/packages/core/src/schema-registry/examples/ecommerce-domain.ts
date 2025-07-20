import { defineEvent, defineCommand, defineProjector } from '../index';
import { z } from 'zod';
import { ok, err } from 'neverthrow';
import { PartitionKeys } from '../../documents/partition-keys';
import { ValidationError } from '../../result/errors';

// ============================================
// Value Objects
// ============================================

export const Money = z.object({
  amount: z.number().positive(),
  currency: z.string().length(3)
});

export const Address = z.object({
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
    items: z.array(z.object({
      productId: z.string(),
      productName: z.string(),
      quantity: z.number().int().positive(),
      unitPrice: Money,
      subtotal: Money
    })),
    shippingAddress: Address,
    totalAmount: Money,
    createdAt: z.string().datetime()
  })
});

export const OrderItemAdded = defineEvent({
  type: 'OrderItemAdded',
  schema: z.object({
    orderId: z.string(),
    item: z.object({
      productId: z.string(),
      productName: z.string(),
      quantity: z.number().int().positive(),
      unitPrice: Money,
      subtotal: Money
    }),
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
      reservations: {}
    }),
    
    InventoryRestocked: (state, event) => ({
      ...state,
      aggregateType: 'Inventory' as const,
      quantity: event.newQuantity,
      lastRestockedAt: event.restockedAt
    }),
    
    InventoryReserved: (state, event) => ({
      ...state,
      aggregateType: 'Inventory' as const,
      quantity: event.newQuantity,
      reservations: {
        ...(state as any).reservations,
        [event.orderId]: event.quantity
      }
    }),
    
    InventoryReleased: (state, event) => {
      const { [event.orderId]: removed, ...remainingReservations } = (state as any).reservations || {};
      return {
        ...state,
        aggregateType: 'Inventory' as const,
        quantity: event.newQuantity,
        reservations: remainingReservations
      };
    }
  }
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
  projector: OrderProjector,
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
    
    handle: (data, context) => {
      const orderId = context.getPartitionKeys().aggregateId;
      
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
  projector: OrderProjector,
  handlers: {
    specifyPartitionKeys: (data) => PartitionKeys.existing(data.orderId, 'Order'),
    
    validate: (data) => ok(undefined),
    
    handle: (data, context) => {
      // Get the aggregate from context
      const aggregateResult = context.getAggregate();
      if (aggregateResult.isErr()) {
        return err(aggregateResult.error);
      }
      
      const aggregate = aggregateResult.value;
      const orderState = aggregate.payload as any;
      
      // Check if order exists (not empty)
      if (orderState.aggregateType === 'Empty') {
        return err(new ValidationError('Order not found'));
      }
      
      // Check if order can be shipped
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
  projector: InventoryProjector,
  handlers: {
    specifyPartitionKeys: () => PartitionKeys.generate('Inventory'),
    
    validate: (data) => ok(undefined),
    
    handle: (data, context) => {
      const productId = context.getPartitionKeys().aggregateId;
      
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
  projector: InventoryProjector,
  handlers: {
    specifyPartitionKeys: (data) => PartitionKeys.existing(data.productId, 'Inventory'),
    
    validate: (data) => ok(undefined),
    
    handle: (data, context) => {
      const aggregateResult = context.getAggregate();
      if (aggregateResult.isErr()) {
        return err(aggregateResult.error);
      }
      const aggregate = aggregateResult.value;
      const currentState = aggregate.payload as any;
      
      if (currentState.aggregateType === 'Empty') {
        return err(new ValidationError('Inventory item not found'));
      }
      
      const newQuantity = currentState.quantity + data.quantity;
      
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
// Multi-Projection Queries
// ============================================

export const LowStockItemsQuery = {
  type: 'LowStockItems',
  handle: async (events: any[]) => {
    // This would be implemented with proper multi-projection logic
    // For now, just return empty result
    return ok({ data: [] });
  }
};

export const OrdersByCustomerQuery = {
  type: 'OrdersByCustomer',
  handle: async (customerId: string, events: any[]) => {
    // This would be implemented with proper multi-projection logic
    // For now, just return empty result
    return ok({ data: [] });
  }
};