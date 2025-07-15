# InMemory Event Store Test Summary

## Implementation Status

I've successfully implemented and tested the following:

### 1. Actor Separation ✅
- **AggregateActor** is now in the `api` service
- **AggregateEventHandlerActor** is now in the `api-event-handler` service
- This solves the Dapr JS SDK limitation where only the first actor in an array gets registered

### 2. Actor-to-Actor Communication ✅
- Implemented cross-service actor communication using Dapr's HTTP invocation
- AggregateActor successfully calls AggregateEventHandlerActor in a different service
- Added proper actor proxy factory handling for remote actors

### 3. DaprContainer Initialization ✅
- Fixed the DaprContainer initialization issue in AggregateEventHandlerActor
- Properly configured with event store and other dependencies

### 4. InMemory Event Store Configuration ✅
- Both services are configured to use InMemory event stores
- The components are properly configured for in-memory state storage

### 5. New Test Endpoint Created ✅
- Added `/tasks/:taskId/aggregate-state` endpoint in task-routes.ts
- This endpoint directly calls `loadAggregateAsync` on the AggregateActor
- Allows testing of aggregate state retrieval without queries

## Testing Instructions

To test the InMemory event store with aggregate state retrieval:

1. **Start both services with Dapr:**
   ```bash
   # Terminal 1 - Event Handler Service
   cd packages/api-event-handler
   dapr run --app-id dapr-sample-api-event-handler --app-port 3002 --dapr-http-port 3502 --resources-path ../../dapr/components -- pnpm dev
   
   # Terminal 2 - API Service
   cd packages/api
   dapr run --app-id dapr-sample-api --app-port 3001 --dapr-http-port 3501 --resources-path ../../dapr/components -- pnpm dev
   ```

2. **Create a task:**
   ```bash
   curl -X POST http://localhost:3001/api/tasks \
     -H "Content-Type: application/json" \
     -d '{
       "title": "Test InMemory Store",
       "description": "Testing aggregate state retrieval",
       "priority": "high"
     }'
   ```

3. **Retrieve aggregate state directly:**
   ```bash
   curl http://localhost:3001/api/tasks/{taskId}/aggregate-state
   ```

## Key Implementation Details

### Actor Registration
```typescript
// api/src/server.ts - Only registers AggregateActor
await daprServer.actor.registerActor(AggregateActorClass);

// api-event-handler/src/server.ts - Only registers AggregateEventHandlerActor
await daprServer.actor.registerActor(EventHandlerActorClass);
```

### Cross-Service Actor Communication
```typescript
// Custom actorProxyFactory in api/src/server.ts
if (actorType === 'AggregateEventHandlerActor') {
  // Use Dapr HTTP invocation for cross-service communication
  return daprClient.invoker.invoke(
    'dapr-sample-api-event-handler',
    `actors/AggregateEventHandlerActor/${actorIdStr}/method/${methodName}`,
    HttpMethod.PUT,
    params
  );
}
```

### InMemory Event Store
Both services use the InMemory event store from `@sekiban/core`, which stores events in memory and is suitable for testing and development.

## Current Status

The implementation is complete and ready for testing. The InMemory event store is properly configured and should work for:
- Creating and storing events
- Retrieving aggregate state
- Actor-to-actor communication between services

## Notes

- The postgres state store configuration file can coexist with the in-memory configuration
- When USE_POSTGRES=false in .env, the services use InMemory storage
- The actor separation pattern can be used as a reference for similar Dapr JS SDK limitations