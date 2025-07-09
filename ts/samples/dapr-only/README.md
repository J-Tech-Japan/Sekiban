# Dapr Actor Sample (TypeScript)

A complete example demonstrating Dapr actors in TypeScript with both direct actor access and API endpoints using DaprClient.

## Key Features

- Simple counter actor with increment/decrement/reset operations
- In-memory state store for development
- Proper handling of both PUT and POST requests to actor methods
- API endpoints that use DaprClient to invoke actors
- Single Express server hosting both actors and API endpoints
- Works with all Dapr invocation methods

## Architecture

### The HTTP Method Issue

Dapr actors are designed to be invoked with PUT requests:
- Direct actor calls: `PUT /actors/{actorType}/{actorId}/method/{method}`
- Dapr SDK registers only PUT routes for actor methods

However, Dapr's service invocation API uses POST:
- Service invocation: `POST /v1.0/invoke/{appId}/method/{method}`
- The `dapr invoke` CLI also uses POST

### Solution

This sample includes middleware that converts POST requests to PUT for actor endpoints:

```typescript
app.use((req, res, next) => {
  if (req.method === 'POST' && req.path.startsWith('/actors/')) {
    req.method = 'PUT';
  }
  next();
});
```

### DaprClient Usage

The API endpoints use DaprClient's `invoker.invoke()` method to call actors:

```typescript
const daprClient = new DaprClient({
  daprHost: "127.0.0.1",
  daprPort: "3500",
  communicationProtocol: CommunicationProtocolEnum.HTTP,
});

// Call an actor method
const result = await daprClient.invoker.invoke(
  'counter-app',                              // app-id
  `actors/CounterActor/${actorId}/method/getCount`, // method path
  'PUT',                                       // HTTP method (actors require PUT)
  {}                                          // request body
);
```

## Running the Sample

1. Install dependencies:
   ```bash
   pnpm install
   ```

2. Start Dapr placement service (if not already running):
   ```bash
   dapr init
   ```

3. Run the app with Dapr:
   ```bash
   ./run-with-dapr.sh
   ```

## Testing

### Using the test scripts:
```bash
# Test API endpoints (recommended)
./test-api.sh

# Test with PUT directly 
./test-put-sidecar.sh

# Test all invocation methods
./test-comprehensive.sh
```

### API Endpoints (Port 3000):
```bash
# Get counter value
curl http://localhost:3000/api/counter/my-counter

# Increment counter
curl -X POST http://localhost:3000/api/counter/my-counter/increment

# Decrement counter
curl -X POST http://localhost:3000/api/counter/my-counter/decrement

# Reset counter
curl -X POST http://localhost:3000/api/counter/my-counter/reset
```

### Direct Actor Access:
```bash
# Direct PUT to app (port 3000)
curl -X PUT http://localhost:3000/actors/CounterActor/test-1/method/getCount \
  -H "Content-Type: application/json" -d '{}'

# PUT via Dapr sidecar (port 3500)
curl -X PUT http://localhost:3500/v1.0/actors/CounterActor/test-1/method/increment \
  -H "Content-Type: application/json" -d '{}'

# Using Dapr CLI (works with the middleware)
dapr invoke --app-id counter-app --method "actors/CounterActor/test-1/method/getCount" --verb POST
```

## Files

- `src/counter-actor.ts` - Simple counter actor implementation
- `src/server.ts` - Express server with Dapr actor hosting and POST->PUT middleware
- `dapr/components/` - Dapr component configurations
- `test-*.sh` - Various test scripts

## Important Notes

1. **Single Server Architecture**: Both actors and API endpoints run on the same Express server (port 3000)
2. **DaprServer Initialization**: Must call `actor.init()` BEFORE registering actors
3. **HTTP Methods**: Actors require PUT requests; middleware converts POST to PUT for compatibility
4. **API Pattern**: API endpoints use `DaprClient.invoker.invoke()` to call actors through Dapr sidecar (port 3500)
5. **State Management**: State is persisted in Dapr's state store (in-memory for this sample)
6. **DaprClient Usage**: Uses `invoker.invoke()` method with HTTP protocol (proxy is not supported for HTTP)