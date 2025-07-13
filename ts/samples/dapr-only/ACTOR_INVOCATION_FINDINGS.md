# Actor Invocation Findings

## Problem Summary
The `DaprClient.invoker.invoke()` method returns 500 errors when calling actor methods, even though the actor methods execute successfully and return the correct results.

## Root Cause
The issue stems from using the wrong API approach for invoking actors. The `DaprClient.invoker` is designed for service-to-service invocation, not for actor invocation.

## Solution: Use ActorProxyBuilder

### Incorrect Approach (causes 500 errors):
```typescript
const result = await daprClient.invoker.invoke(
  process.env.DAPR_APP_ID || 'counter-di-app',
  `actors/CounterActorWithDI/${actorId}/method/increment`,
  HttpMethod.PUT,
  {}
);
```

### Correct Approach (works properly):
```typescript
// Define actor interface
interface CounterActorInterface {
  increment(): Promise<number>;
  decrement(): Promise<number>;
  getCount(): Promise<number>;
  reset(): Promise<void>;
  testDI(): Promise<object>;
}

// Create ActorProxyBuilder
const actorProxyBuilder = new ActorProxyBuilder<CounterActorInterface>(
  CounterActorWithDI, 
  daprClient
);

// Use the proxy to invoke methods
const actor = actorProxyBuilder.build(new ActorId(actorId));
const result = await actor.increment();
```

## Key Differences

1. **API Path**: 
   - `invoker`: Uses service invocation path (incorrectly trying to call actor endpoints)
   - `ActorProxyBuilder`: Uses proper actor API path through Dapr's actor subsystem

2. **Error Handling**:
   - `invoker`: Returns 500 errors due to incorrect API usage
   - `ActorProxyBuilder`: Handles actor communication properly

3. **Type Safety**:
   - `invoker`: No type safety for actor methods
   - `ActorProxyBuilder`: Full TypeScript type safety through interfaces

## Implementation Example

See `server-with-proxy.ts` for a complete working example using ActorProxyBuilder with Awilix DI.

## Recommendation

Always use `ActorProxyBuilder` for actor invocation in Dapr applications. The `invoker` should only be used for service-to-service calls, not for actor method invocation.