# Dapr Actor Integration Fixes

This document summarizes the fixes applied to make Dapr actors work correctly in the dapr-sample project, based on lessons learned from the dapr-only sample.

## Key Issues and Fixes

### 1. Actor Initialization Order (✅ Fixed)
**Problem**: Actors must be initialized before registration.
**Fix**: In `server.ts`, changed to call `actor.init()` BEFORE `registerActor()`:
```typescript
// CRITICAL: Initialize actor runtime FIRST (before registering actors)
await daprServer.actor.init();
logger.info('Actor runtime initialized');

// Register actors AFTER init
daprServer.actor.registerActor(AggregateActorFactory.createActorClass());
```

### 2. HTTP Method for Actor Calls (✅ Fixed)
**Problem**: Dapr actors require PUT requests for method invocation, but many tools send POST.
**Fix**: Added middleware to convert POST to PUT for actor method calls:
```typescript
app.use((req, res, next) => {
  if (req.path.includes('/method/') && req.method === 'POST') {
    req.method = 'PUT';
    logger.debug(`Converted POST to PUT for actor method: ${req.path}`);
  }
  next();
});
```

### 3. SekibanDaprExecutor Actor Invocation (✅ Fixed)
**Problem**: Using incorrect HTTP method for actor calls.
**Fix**: In `sekiban-dapr-executor.ts`, using direct HTTP calls with PUT method:
```typescript
const response = await fetch(url, {
  method: 'PUT', // CRITICAL: Actors require PUT for method calls
  headers: {
    'Content-Type': 'application/json',
  },
  body: JSON.stringify(data)
});
```

### 4. Actor Proxy Factory (✅ Fixed)
**Problem**: Trying to use non-existent `daprClient.actor.createProxy` method.
**Fix**: Created a custom proxy that uses `invoker.invoke()` with PUT method:
```typescript
const actorProxyFactory = {
  createActorProxy: (actorId: any, actorType: string) => {
    return {
      invoke: async (methodName: string, data: any) => {
        const actorIdStr = actorId.id || actorId;
        return daprClient.invoker.invoke(
          config.DAPR_APP_ID,
          `actors/${actorType}/${actorIdStr}/method/${methodName}`,
          'PUT',
          data
        );
      }
    };
  }
};
```

## Architecture Summary

The fixed architecture now works as follows:

1. **Single Express Server**: Hosts both API endpoints and actor endpoints on the same port (3000)
2. **Dapr Sidecar**: Runs on port 3500 and manages actor placement and state
3. **Actor Invocation Flow**: 
   - API endpoint → DaprClient → Dapr Sidecar → Actor (via PUT request)
   - Actor methods are registered as PUT endpoints by DaprServer

## Testing

Run the test script to verify the fixes:
```bash
./test-actor-integration.sh
```

This will test:
- Creating tasks (uses actors)
- Getting task details (uses actors)
- Updating tasks (uses actors)
- Listing all tasks

## Important Notes

1. **Always use PUT for actor methods** - This is a Dapr requirement
2. **Initialize actors before registration** - Order matters!
3. **DaprClient limitations**: 
   - `proxy` feature doesn't work with HTTP protocol
   - Use `invoker.invoke()` with PUT method instead
4. **Middleware placement**: POST-to-PUT conversion must happen before DaprServer setup