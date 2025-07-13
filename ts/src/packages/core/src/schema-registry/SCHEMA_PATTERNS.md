# Schema-First Patterns for Sekiban TypeScript

This guide provides best practices and patterns for using Sekiban's schema-first approach with Zod validation.

## Table of Contents

1. [Core Concepts](#core-concepts)
2. [Event Patterns](#event-patterns)
3. [Command Patterns](#command-patterns)
4. [Projector Patterns](#projector-patterns)
5. [Multi-Projection Queries](#multi-projection-queries)
6. [Testing Strategies](#testing-strategies)
7. [Performance Considerations](#performance-considerations)

## Core Concepts

### Schema-First Design

The schema-first approach means defining your domain types as data schemas rather than classes:

```typescript
// ❌ Traditional class-based approach
class UserCreatedEvent {
  constructor(
    public userId: string,
    public email: string
  ) {}
}

// ✅ Schema-first approach
const UserCreatedEvent = defineEvent({
  type: 'UserCreated',
  schema: z.object({
    userId: z.string(),
    email: z.string().email()
  })
});
```

### Benefits

1. **Runtime Validation**: Automatic validation at boundaries
2. **Type Inference**: Full TypeScript types from schemas
3. **Serialization**: Plain objects serialize naturally
4. **Composability**: Easy to combine and extend schemas

## Event Patterns

### Basic Event Definition

```typescript
export const OrderPlaced = defineEvent({
  type: 'OrderPlaced',
  schema: z.object({
    orderId: z.string(),
    customerId: z.string(),
    items: z.array(z.object({
      productId: z.string(),
      quantity: z.number().int().positive(),
      price: z.number().positive()
    })),
    totalAmount: z.number().positive(),
    placedAt: z.string().datetime()
  })
});
```

### Event Versioning

When events need to evolve, use versioning:

```typescript
// Version 1
export const UserRegisteredV1 = defineEvent({
  type: 'UserRegistered',
  schema: z.object({
    userId: z.string(),
    email: z.string().email()
  })
});

// Version 2 - adds optional phone field
export const UserRegisteredV2 = defineEvent({
  type: 'UserRegistered',
  schema: z.object({
    userId: z.string(),
    email: z.string().email(),
    phone: z.string().optional()
  })
});
```

### Event Metadata

Include metadata in events for auditing:

```typescript
const AuditableEventSchema = z.object({
  metadata: z.object({
    userId: z.string(),
    timestamp: z.string().datetime(),
    ipAddress: z.string().ip().optional(),
    userAgent: z.string().optional()
  })
});

export const SensitiveActionPerformed = defineEvent({
  type: 'SensitiveActionPerformed',
  schema: AuditableEventSchema.extend({
    action: z.string(),
    resourceId: z.string()
  })
});
```

## Command Patterns

### Command with Validation Layers

Commands should validate at multiple levels:

```typescript
export const TransferMoney = defineCommand({
  type: 'TransferMoney',
  schema: z.object({
    fromAccountId: z.string(),
    toAccountId: z.string(),
    amount: z.number().positive(),
    currency: z.string().length(3),
    reference: z.string().optional()
  }),
  handlers: {
    specifyPartitionKeys: (data) => 
      PartitionKeys.existing(data.fromAccountId, 'Account'),
    
    validate: (data) => {
      // Business rules beyond schema
      if (data.amount > 10000) {
        return err(new ValidationError('Transfers over $10,000 require approval'));
      }
      
      if (data.fromAccountId === data.toAccountId) {
        return err(new ValidationError('Cannot transfer to same account'));
      }
      
      return ok(undefined);
    },
    
    handle: (data, aggregate) => {
      const balance = (aggregate.payload as any).balance || 0;
      
      if (balance < data.amount) {
        return err(new ValidationError('Insufficient funds'));
      }
      
      return ok([
        MoneyTransferred.create({
          fromAccountId: data.fromAccountId,
          toAccountId: data.toAccountId,
          amount: data.amount,
          currency: data.currency,
          reference: data.reference,
          transferredAt: new Date().toISOString()
        })
      ]);
    }
  }
});
```

### Conditional Commands

Commands that only work on specific aggregate states:

```typescript
export const ApproveOrder = defineCommand({
  type: 'ApproveOrder',
  schema: z.object({
    orderId: z.string(),
    approvedBy: z.string()
  }),
  handlers: {
    specifyPartitionKeys: (data) => 
      PartitionKeys.existing(data.orderId, 'Order'),
    
    validate: (data, aggregate) => {
      const state = aggregate?.payload as any;
      
      if (!state || state.aggregateType === 'Empty') {
        return err(new ValidationError('Order not found'));
      }
      
      if (state.status !== 'pending_approval') {
        return err(new ValidationError('Order is not pending approval'));
      }
      
      return ok(undefined);
    },
    
    handle: (data) => ok([
      OrderApproved.create({
        orderId: data.orderId,
        approvedBy: data.approvedBy,
        approvedAt: new Date().toISOString()
      })
    ])
  }
});
```

### Batch Commands

Commands that generate multiple events:

```typescript
export const FulfillOrder = defineCommand({
  type: 'FulfillOrder',
  schema: z.object({
    orderId: z.string(),
    warehouseId: z.string(),
    shipmentDetails: z.object({
      carrier: z.string(),
      trackingNumber: z.string(),
      estimatedDelivery: z.string().datetime()
    })
  }),
  handlers: {
    specifyPartitionKeys: (data) => 
      PartitionKeys.existing(data.orderId, 'Order'),
    
    validate: () => ok(undefined),
    
    handle: (data, aggregate) => {
      const events = [];
      
      // Mark items as picked
      events.push(
        OrderItemsPicked.create({
          orderId: data.orderId,
          warehouseId: data.warehouseId,
          pickedAt: new Date().toISOString()
        })
      );
      
      // Mark as packed
      events.push(
        OrderPacked.create({
          orderId: data.orderId,
          packedAt: new Date().toISOString()
        })
      );
      
      // Ship the order
      events.push(
        OrderShipped.create({
          orderId: data.orderId,
          ...data.shipmentDetails
        })
      );
      
      return ok(events);
    }
  }
});
```

## Projector Patterns

### State Machines with Projectors

Model different aggregate states as different types:

```typescript
type SubscriptionState = 
  | { aggregateType: 'Empty' }
  | { aggregateType: 'Trial'; trialEndsAt: string; features: string[] }
  | { aggregateType: 'Active'; plan: string; renewsAt: string }
  | { aggregateType: 'Suspended'; reason: string; suspendedAt: string }
  | { aggregateType: 'Cancelled'; cancelledAt: string };

export const SubscriptionProjector = defineProjector<SubscriptionState>({
  aggregateType: 'Subscription',
  initialState: () => ({ aggregateType: 'Empty' }),
  projections: {
    TrialStarted: (state, event) => ({
      aggregateType: 'Trial',
      trialEndsAt: event.trialEndsAt,
      features: event.features
    }),
    
    SubscriptionActivated: (state, event) => ({
      aggregateType: 'Active',
      plan: event.plan,
      renewsAt: event.renewsAt
    }),
    
    SubscriptionSuspended: (state, event) => {
      // Only active subscriptions can be suspended
      if (state.aggregateType === 'Active') {
        return {
          aggregateType: 'Suspended',
          reason: event.reason,
          suspendedAt: event.suspendedAt
        };
      }
      return state;
    },
    
    SubscriptionCancelled: (state, event) => ({
      aggregateType: 'Cancelled',
      cancelledAt: event.cancelledAt
    })
  }
});
```

### Computed Properties

Calculate derived values in projections:

```typescript
export const ShoppingCartProjector = defineProjector({
  aggregateType: 'ShoppingCart',
  initialState: () => ({ aggregateType: 'Empty' as const }),
  projections: {
    CartCreated: (state, event) => ({
      aggregateType: 'ShoppingCart' as const,
      cartId: event.cartId,
      customerId: event.customerId,
      items: [] as CartItem[],
      totalItems: 0,
      totalAmount: 0,
      lastModified: event.createdAt
    }),
    
    ItemAdded: (state, event) => {
      if (state.aggregateType !== 'ShoppingCart') return state;
      
      const existingItem = state.items.find(i => i.productId === event.productId);
      const items = existingItem
        ? state.items.map(i => 
            i.productId === event.productId
              ? { ...i, quantity: i.quantity + event.quantity }
              : i
          )
        : [...state.items, {
            productId: event.productId,
            name: event.productName,
            price: event.price,
            quantity: event.quantity
          }];
      
      // Recompute totals
      const totalItems = items.reduce((sum, item) => sum + item.quantity, 0);
      const totalAmount = items.reduce((sum, item) => sum + (item.price * item.quantity), 0);
      
      return {
        ...state,
        items,
        totalItems,
        totalAmount,
        lastModified: event.addedAt
      };
    }
  }
});
```

### Snapshot Optimization

For aggregates with many events, use snapshots:

```typescript
export const DocumentProjector = defineProjector({
  aggregateType: 'Document',
  initialState: () => ({ aggregateType: 'Empty' as const }),
  
  // Indicate snapshot support
  supportsSnapshots: true,
  snapshotFrequency: 50, // Create snapshot every 50 events
  
  projections: {
    DocumentCreated: (state, event) => ({
      aggregateType: 'Document' as const,
      documentId: event.documentId,
      title: event.title,
      content: event.content,
      version: 1,
      revisions: []
    }),
    
    DocumentEdited: (state, event) => {
      if (state.aggregateType !== 'Document') return state;
      
      return {
        ...state,
        content: event.newContent,
        version: state.version + 1,
        revisions: [
          ...state.revisions,
          {
            version: state.version,
            content: state.content,
            editedAt: event.editedAt,
            editedBy: event.editedBy
          }
        ]
      };
    }
  }
});
```

## Multi-Projection Queries

### Cross-Aggregate Analytics

```typescript
export class RevenueByProductQuery implements IMultiProjectionQuery<
  any, any, 
  Array<{ productId: string; productName: string; revenue: number; unitsSold: number }>
> {
  constructor(
    private startDate: string,
    private endDate: string
  ) {}

  query = async (events: IEvent[]) => {
    const productRevenue = new Map<string, {
      productName: string;
      revenue: number;
      unitsSold: number;
    }>();

    for (const event of events) {
      if (event.eventType === 'OrderPlaced') {
        const orderDate = event.payload.placedAt;
        
        // Filter by date range
        if (orderDate < this.startDate || orderDate > this.endDate) {
          continue;
        }
        
        // Aggregate revenue by product
        for (const item of event.payload.items) {
          const current = productRevenue.get(item.productId) || {
            productName: item.productName,
            revenue: 0,
            unitsSold: 0
          };
          
          productRevenue.set(item.productId, {
            productName: item.productName,
            revenue: current.revenue + (item.price * item.quantity),
            unitsSold: current.unitsSold + item.quantity
          });
        }
      }
    }

    // Convert to array and sort by revenue
    return Array.from(productRevenue.entries())
      .map(([productId, data]) => ({ productId, ...data }))
      .sort((a, b) => b.revenue - a.revenue);
  };
}
```

### Real-time Dashboards

```typescript
export class DashboardMetricsQuery implements IMultiProjectionQuery<any, any, {
  totalOrders: number;
  totalRevenue: number;
  activeCustomers: number;
  topProducts: Array<{ productId: string; name: string; quantity: number }>;
  ordersByStatus: Record<string, number>;
}> {
  query = async (events: IEvent[]) => {
    const metrics = {
      totalOrders: 0,
      totalRevenue: 0,
      activeCustomers: new Set<string>(),
      productQuantities: new Map<string, { name: string; quantity: number }>(),
      ordersByStatus: {} as Record<string, number>
    };

    // Build order states for status counting
    const orderStates = new Map<string, string>();

    for (const event of events) {
      switch (event.eventType) {
        case 'OrderPlaced':
          metrics.totalOrders++;
          metrics.totalRevenue += event.payload.totalAmount;
          metrics.activeCustomers.add(event.payload.customerId);
          orderStates.set(event.payload.orderId, 'placed');
          
          // Track product quantities
          for (const item of event.payload.items) {
            const current = metrics.productQuantities.get(item.productId) || {
              name: item.productName,
              quantity: 0
            };
            metrics.productQuantities.set(item.productId, {
              name: item.productName,
              quantity: current.quantity + item.quantity
            });
          }
          break;
          
        case 'OrderShipped':
          orderStates.set(event.payload.orderId, 'shipped');
          break;
          
        case 'OrderDelivered':
          orderStates.set(event.payload.orderId, 'delivered');
          break;
          
        case 'OrderCancelled':
          orderStates.set(event.payload.orderId, 'cancelled');
          break;
      }
    }

    // Count orders by status
    for (const status of orderStates.values()) {
      metrics.ordersByStatus[status] = (metrics.ordersByStatus[status] || 0) + 1;
    }

    // Get top 5 products
    const topProducts = Array.from(metrics.productQuantities.entries())
      .map(([productId, data]) => ({ productId, ...data }))
      .sort((a, b) => b.quantity - a.quantity)
      .slice(0, 5);

    return {
      totalOrders: metrics.totalOrders,
      totalRevenue: metrics.totalRevenue,
      activeCustomers: metrics.activeCustomers.size,
      topProducts,
      ordersByStatus: metrics.ordersByStatus
    };
  };
}
```

## Testing Strategies

### Testing Events

```typescript
describe('Event Schema Validation', () => {
  it('validates event data', () => {
    const validData = {
      userId: '123',
      email: 'user@example.com'
    };
    
    const event = UserCreated.create(validData);
    expect(event.type).toBe('UserCreated');
    expect(event.userId).toBe('123');
    
    // Test invalid data
    expect(() => {
      UserCreated.create({
        userId: '123',
        email: 'not-an-email'
      });
    }).toThrow();
  });
});
```

### Testing Commands

```typescript
describe('Command Validation', () => {
  it('validates business rules', () => {
    const result = TransferMoney.validate({
      fromAccountId: 'acc-1',
      toAccountId: 'acc-1', // Same account
      amount: 100,
      currency: 'USD'
    });
    
    expect(result.isErr()).toBe(true);
    expect(result._unsafeUnwrapErr().message).toContain('same account');
  });
});
```

### Testing Projectors

```typescript
describe('Projector State Transitions', () => {
  it('handles event sequences', () => {
    const projector = OrderProjector;
    let state = projector.initialState();
    
    // Apply events
    const createResult = projector.project(state, {
      eventType: 'OrderCreated',
      payload: { /* ... */ }
    });
    
    expect(createResult.isOk()).toBe(true);
    state = createResult._unsafeUnwrap();
    
    expect(state.payload.aggregateType).toBe('Order');
    expect(state.payload.status).toBe('pending');
  });
});
```

## Performance Considerations

### Schema Caching

Schemas are parsed once and cached:

```typescript
// ✅ Good - schema defined once
const UserSchema = z.object({
  id: z.string(),
  name: z.string()
});

export const UserCreated = defineEvent({
  type: 'UserCreated',
  schema: UserSchema
});

// ❌ Bad - schema recreated each time
function createUserEvent() {
  return defineEvent({
    type: 'UserCreated',
    schema: z.object({
      id: z.string(),
      name: z.string()
    })
  });
}
```

### Lazy Loading

For large schemas, use lazy loading:

```typescript
const LazyProductSchema = z.lazy(() => 
  z.object({
    id: z.string(),
    name: z.string(),
    category: CategorySchema,
    variants: z.array(VariantSchema)
  })
);
```

### Efficient Queries

Optimize multi-projection queries:

```typescript
export class EfficientQuery implements IMultiProjectionQuery<any, any, Result> {
  query = async (events: IEvent[]) => {
    // Use early exits
    const relevantEvents = events.filter(e => 
      e.eventType === 'OrderPlaced' && 
      e.payload.totalAmount > 100
    );
    
    // Use Map for O(1) lookups
    const aggregates = new Map<string, any>();
    
    // Process in single pass
    for (const event of relevantEvents) {
      // Process event
    }
    
    return Array.from(aggregates.values());
  };
}
```

## Best Practices

1. **Keep Schemas Simple**: Avoid deeply nested structures
2. **Use Composition**: Build complex schemas from simple ones
3. **Version Events**: Never modify existing event schemas
4. **Validate Early**: Use schema validation at system boundaries
5. **Document Schemas**: Add descriptions to schema fields
6. **Test Thoroughly**: Test both valid and invalid scenarios
7. **Monitor Performance**: Profile schema validation in production