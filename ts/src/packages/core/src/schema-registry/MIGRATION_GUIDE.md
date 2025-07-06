# Migration Guide: Unified Command Design

This guide helps you migrate from the old command definition style to the new unified design that aligns with C# ICommandWithHandler.

## Key Changes

1. **Projector is now required** - No more manual `aggregateType` specification
2. **TypedPartitionKeys** - Type-safe partition key generation using projector type
3. **Context-based handlers** - Commands receive context instead of raw aggregate
4. **Payload type constraints** - Optional type safety for state transitions

## Migration Examples

### Before (Old Style)

```typescript
const CreateOrder = defineCommand({
  type: 'CreateOrder',
  schema: z.object({
    customerId: z.string(),
    items: z.array(OrderItem)
  }),
  aggregateType: 'Order', // Manual specification
  handlers: {
    specifyPartitionKeys: () => PartitionKeys.generate('Order'),
    validate: (data) => ok(undefined),
    handle: (data, aggregate) => { // Direct aggregate access
      const orderId = aggregate.partitionKeys.aggregateId;
      return ok([OrderCreated.create({ orderId, ...data })]);
    }
  }
});
```

### After (New Style)

```typescript
const CreateOrder = defineCommand({
  type: 'CreateOrder',
  schema: z.object({
    customerId: z.string(),
    items: z.array(OrderItem),
    tenantId: z.string().optional()
  }),
  projector: OrderProjector, // Required projector
  handlers: {
    // Type-safe partition keys with optional tenant
    specifyPartitionKeys: (data) => data.tenantId 
      ? TypedPartitionKeys.Generate(OrderProjector, data.tenantId)
      : TypedPartitionKeys.Generate(OrderProjector),
    
    validate: (data) => ok(undefined),
    
    // Context-based handler
    handle: (data, context) => {
      const orderId = context.getPartitionKeys().aggregateId;
      return ok([OrderCreated.create({ orderId, ...data })]);
    }
  }
});
```

## State Transition Commands

### Before

```typescript
const ShipOrder = defineCommand({
  type: 'ShipOrder',
  schema: z.object({ orderId: z.string() }),
  aggregateType: 'Order',
  handlers: {
    specifyPartitionKeys: (data) => PartitionKeys.existing(data.orderId, 'Order'),
    handle: (data, aggregate) => {
      // Manual state checking
      const state = aggregate.payload as any;
      if (state.status !== 'confirmed') {
        return err(new ValidationError('Order must be confirmed'));
      }
      return ok([OrderShipped.create({ orderId: data.orderId })]);
    }
  }
});
```

### After

```typescript
const ShipOrder = defineCommand({
  type: 'ShipOrder',
  schema: z.object({ orderId: z.string() }),
  projector: OrderProjector,
  requiredPayloadType: 'ConfirmedOrder', // Type constraint
  handlers: {
    specifyPartitionKeys: (data) => 
      TypedPartitionKeys.Existing(OrderProjector, data.orderId),
    
    handle: (data, context) => {
      // context.getAggregate() returns Aggregate<ConfirmedOrder>
      // Type checking is automatic
      return ok([OrderShipped.create({ orderId: data.orderId })]);
    }
  }
});
```

## Using Simplified API

For common patterns, use the simplified API:

```typescript
// Creation command
const CreateUser = command.create('CreateUser', {
  schema: z.object({
    name: z.string(),
    email: z.string()
  }),
  projector: UserProjector,
  partitionKeys: () => TypedPartitionKeys.Generate(UserProjector),
  handle: (data, { aggregateId, appendEvent }) => {
    appendEvent(UserCreated.create({
      userId: aggregateId,
      name: data.name,
      email: data.email
    }));
  }
});

// Update command
const UpdateUser = command.update('UpdateUser', {
  schema: z.object({
    userId: z.string(),
    name: z.string()
  }),
  projector: UserProjector,
  partitionKeys: (data) => TypedPartitionKeys.Existing(UserProjector, data.userId),
  handle: (data, { appendEvent }) => {
    appendEvent(UserUpdated.create({
      userId: data.userId,
      name: data.name
    }));
  }
});

// State transition
const ActivateUser = command.transition('ActivateUser', {
  schema: z.object({ userId: z.string() }),
  projector: UserProjector,
  fromState: 'InactiveUser',
  partitionKeys: (data) => TypedPartitionKeys.Existing(UserProjector, data.userId),
  handle: (data, { aggregate, appendEvent }) => {
    // aggregate is typed as InactiveUser
    appendEvent(UserActivated.create({ userId: data.userId }));
  }
});
```

## Executor Migration

### Before

```typescript
const executor = new CommandExecutor(eventStore, aggregateLoader);
const result = await executor.execute(command, context);
```

### After

```typescript
// Use unified executor directly
const executor = createUnifiedExecutor(eventStore, aggregateLoader);
const commandInstance = CreateOrder.create({ ... });
const result = await executor.execute(commandInstance);

// Or use adapter for backward compatibility
const adapter = createExecutorAdapter(executor, serviceProvider);
const result = await adapter.execute(command, context);
```

## Benefits of Migration

1. **Type Safety** - Projector types flow through the entire command lifecycle
2. **Less Boilerplate** - No manual aggregate type specification
3. **Better State Management** - Explicit payload type constraints
4. **Service Injection** - Built-in dependency injection support
5. **C# Alignment** - Consistent design across platforms

## Step-by-Step Migration

1. **Update imports**
   ```typescript
   import { TypedPartitionKeys } from '@sekiban/core';
   ```

2. **Add projector to commands**
   - Replace `aggregateType: 'Foo'` with `projector: FooProjector`

3. **Update partition key generation**
   - Replace `PartitionKeys.generate('Foo')` with `TypedPartitionKeys.Generate(FooProjector)`
   - Replace `PartitionKeys.existing(id, 'Foo')` with `TypedPartitionKeys.Existing(FooProjector, id)`

4. **Update handlers to use context**
   - Change `handle: (data, aggregate)` to `handle: (data, context)`
   - Access aggregate via `context.getAggregate()`
   - Access partition keys via `context.getPartitionKeys()`

5. **Add payload constraints where needed**
   - Add `requiredPayloadType: 'StateName'` for state-specific commands

6. **Test thoroughly**
   - Ensure all commands work with the new executor
   - Verify type constraints are enforced correctly