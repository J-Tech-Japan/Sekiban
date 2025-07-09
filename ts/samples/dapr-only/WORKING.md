# Dapr Actor Sample - Working Configuration

This sample demonstrates a working Dapr actor implementation in TypeScript.

## Key Findings

1. **HTTP Method**: Dapr uses PUT requests for actor method invocations, not POST
2. **DaprServer Integration**: The `serverHttp` option properly integrates with Express
3. **Initialization Order**: Call `actor.init()` before `actor.registerActor()`

## Working Test Commands

```bash
# Start the app with Dapr
./run-with-dapr.sh

# Test via Dapr sidecar (port 3500) - USE PUT!
curl -X PUT http://localhost:3500/v1.0/actors/CounterActor/counter-1/method/getCount \
  -H "Content-Type: application/json" \
  -d '{}'

# Test directly on app (port 3000) - USE PUT!
curl -X PUT http://localhost:3000/actors/CounterActor/counter-1/method/increment \
  -H "Content-Type: application/json" \
  -d '{}'
```

## Why dapr invoke CLI doesn't work

The `dapr invoke` CLI uses POST by default for the `/v1.0/invoke` endpoint, which then gets 
translated to actor calls. But actors require PUT method. Use curl with PUT instead.

## Architecture

- DaprServer creates actor routes at `/actors/{actorType}/{actorId}/method/{method}`
- These routes accept PUT requests
- The actor instance is activated on first call
- State is persisted via Dapr's state management