# C# Sekiban Pub/Sub Setup Guide

This guide explains how pub/sub works in the C# Sekiban implementation with Dapr.

## Architecture Overview

1. **EventHandlerActor** saves events to the event store
2. **DaprEventStore** publishes events to Dapr pub/sub after saving
3. **EventPubSubController** subscribes to events via HTTP endpoints
4. **MultiProjectorActor** receives events for real-time projection updates

## Components

### 1. Event Publishing (DaprEventStore)

When events are saved, they are automatically published to:
- Topic: `events.{EventTypeName}` - for specific event types
- Topic: `events.all` - for all events

### 2. Event Subscription (EventPubSubController)

The controller subscribes to:
- `events.all` - receives all domain events
- `events.*` - pattern subscription for specific event types

### 3. Configuration Files

Two sets of configurations are available:

#### For Development (In-Memory)
Use `dapr-components/` directory:
- In-memory pub/sub
- In-memory state store
- Pre-configured subscription

#### For Production (Redis)
Use `components/` directory:
- Redis pub/sub
- Redis state store
- Requires Redis to be running

## Running the Application

### Option 1: In-Memory (Development)

```bash
dapr run \
  --app-id dapr-sample-api \
  --app-port 5000 \
  --dapr-http-port 3500 \
  --components-path ./dapr-components \
  -- dotnet run --project DaprSample.Api
```

### Option 2: With Redis (Production-like)

First, ensure Redis is running:
```bash
docker run -d -p 6379:6379 redis:alpine
```

Then run:
```bash
dapr run \
  --app-id dapr-sample-api \
  --app-port 5000 \
  --dapr-http-port 3500 \
  --components-path ./components \
  -- dotnet run --project DaprSample.Api
```

## Testing Pub/Sub

### 1. Run the test script
```bash
./test-pubsub.sh
```

### 2. Or test manually

Create a user (this will publish events):
```bash
curl -X POST http://localhost:5000/api/users/create \
  -H "Content-Type: application/json" \
  -d '{
    "userId": "550e8400-e29b-41d4-a716-446655440000",
    "name": "Test User",
    "email": "test@example.com"
  }'
```

Query the multi-projection:
```bash
curl -X GET "http://localhost:5000/api/users/list?nameContains=Test"
```

### 3. Use the test endpoint
```bash
curl -X POST http://localhost:5000/api/test/pubsub-flow
```

This endpoint will:
- Create a user
- Wait 1 second
- Query the multi-projection
- Report if pub/sub is working

## Troubleshooting

### Events not reaching MultiProjectorActor

1. **Check Dapr subscriptions**:
   ```bash
   curl http://localhost:3500/v1.0/subscribe
   ```
   Should show subscriptions to `events.all` and `events.*`

2. **Check component configuration**:
   ```bash
   dapr components -k pubsub
   ```

3. **Check Redis (if using Redis components)**:
   ```bash
   redis-cli ping
   ```

4. **Enable debug logging**:
   Add to `appsettings.Development.json`:
   ```json
   {
     "Logging": {
       "LogLevel": {
         "Default": "Debug",
         "Microsoft.AspNetCore": "Debug",
         "Sekiban": "Debug"
       }
     }
   }
   ```

### EventPubSubController not found

The controller is in the Sekiban.Pure.Dapr assembly. Program.cs has been updated to include:
```csharp
.AddApplicationPart(typeof(Sekiban.Pure.Dapr.Controllers.EventPubSubController).Assembly)
```

## How It Works

1. **Command Execution**: 
   - User sends command to API
   - Command is processed by AggregateEventHandlerActor
   - Events are saved to state store

2. **Event Publishing**:
   - DaprEventStore publishes events to pub/sub
   - Events go to both specific and general topics

3. **Event Subscription**:
   - Dapr routes events to EventPubSubController
   - Controller receives events at `/pubsub/events`

4. **Multi-Projection Update**:
   - Controller forwards events to all MultiProjectorActors
   - Projections are updated in real-time

5. **Query Execution**:
   - Queries are executed against MultiProjectorActor projections
   - No need to replay events for every query

## Benefits

- **Real-time projections**: Events update projections immediately
- **Scalability**: Multiple projectors can process events in parallel
- **Resilience**: Pub/sub provides retry and error handling
- **Decoupling**: Event store and projections are independent