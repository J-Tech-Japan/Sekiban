# DaprSample - Sekiban with Dapr Integration

This sample demonstrates how to use Sekiban Event Sourcing with Dapr actors as an alternative to Orleans.

## Prerequisites

1. **.NET 9 SDK**
2. **Dapr CLI** - Install from https://docs.dapr.io/getting-started/install-dapr-cli/
3. **Docker** - Required for running Redis through Dapr

## Installation

### Install Dapr CLI (if not already installed)

```bash
# Linux/macOS
curl -fsSL https://raw.githubusercontent.com/dapr/cli/master/install/install.sh | bash

# Windows
powershell -Command "iwr -useb https://raw.githubusercontent.com/dapr/cli/master/install/install.ps1 | iex"
```

### Initialize Dapr

```bash
dapr init
```

## Running the Sample

### Quick Start (Recommended)

```bash
# Navigate to the DaprSample directory
cd internalUsages/DaprSample

# Run the interactive launcher
./start-dapr.sh
```

The launcher will prompt you to choose between:
1. **In-Memory** (default) - No dependencies, perfect for development
2. **Redis** - Persistent state, requires Redis server

### Manual Start Options

#### Option 1: In-Memory State Store (Development)

```bash
# Quick start - no external dependencies
./start-dapr-inmemory.sh
```

**Pros:**
- No setup required
- Fast startup
- Perfect for development and testing

**Cons:**
- State is lost when application restarts
- Not suitable for production

#### Option 2: Redis State Store (Production-like)

```bash
# First, start Redis (required)
docker run -d -p 6379:6379 redis:latest

# Then start the application
./start-dapr-redis.sh
```

**Pros:**
- Persistent state across restarts
- Production-ready setup
- Better performance for large datasets

**Cons:**
- Requires Redis server
- Additional setup steps

### State Store Configuration

The project includes two sets of Dapr components:

- **`dapr-components/`** - In-Memory state store configuration
- **`components/`** - Redis state store configuration

### Configuration Details

#### In-Memory Configuration (`dapr-components/statestore.yaml`)
```yaml
spec:
  type: state.in-memory
  metadata:
  - name: actorStateStore
    value: "true"
```

#### Redis Configuration (`components/statestore.yaml`)
```yaml
spec:
  type: state.redis
  metadata:
  - name: redisHost
    value: "localhost:6379"
  - name: actorStateStore
    value: "true"
```

### Available Scripts

| Script | Purpose | Dependencies |
|--------|---------|--------------|
| `./start-dapr.sh` | Interactive launcher | None |
| `./start-dapr-inmemory.sh` | Direct in-memory start | None |
| `./start-dapr-redis.sh` | Direct Redis start | Redis server |
| `./start-dapr-with-placement.sh` | Legacy/debugging | None |

### Option 1: Using .NET Aspire (Recommended)

```bash
dotnet run --project DaprSample.AppHost
```

This will start:
- Redis container for state store and pub/sub
- Dapr sidecar for the API
- DaprSample.Api with Swagger UI

### Option 2: Direct Dapr Run

```bash
# Start Redis
docker run -d -p 6379:6379 redis

# Run the API with Dapr
cd DaprSample.Api
dapr run --app-id sekiban-api --app-port 5000 --dapr-http-port 3500 --components-path ../components -- dotnet run
```

## Testing the API

Once running, you can access:
- Swagger UI: https://localhost:5001/swagger or http://localhost:5000/swagger
- Aspire Dashboard: https://localhost:17045 (when using Aspire)

### Sample API Calls

1. **Create User**
   ```bash
   curl -X POST https://localhost:5001/api/users/create \
     -H "Content-Type: application/json" \
     -d '{
       "userId": "550e8400-e29b-41d4-a716-446655440000",
       "name": "John Doe",
       "email": "john@example.com"
     }'
   ```

2. **Update User Name**
   ```bash
   curl -X POST https://localhost:5001/api/users/550e8400-e29b-41d4-a716-446655440000/update-name \
     -H "Content-Type: application/json" \
     -d '{
       "newName": "Jane Doe"
     }'
   ```

3. **Get User**
   ```bash
   curl https://localhost:5001/api/users/550e8400-e29b-41d4-a716-446655440000
   ```

## Architecture

This sample implements:
- **Event Sourcing** using Dapr actors instead of Orleans grains
- **CQRS** pattern with separate command and query handling
- **Actor-based persistence** with Redis state store
- **Event streaming** with Dapr pub/sub

### Key Components

- `AggregateActor` - Handles command execution and maintains aggregate state
- `AggregateEventHandlerActor` - Manages event persistence and retrieval
- `MultiProjectorActor` - Handles cross-aggregate projections
- `DaprEventStore` - Repository implementation using Dapr actors

## Testing

#### Test In-Memory Setup
```bash
# Quick test of in-memory configuration
./start-dapr-inmemory.sh
# Test in browser: http://localhost:5010/swagger
```

#### Test Redis Setup
```bash
# Automated Redis test (includes Redis connectivity check)
./test-redis.sh
```

#### Manual Testing Endpoints
- **Health Check**: `http://localhost:5010/health`
- **Swagger UI**: `http://localhost:5010/swagger`
- **Create User**: `POST http://localhost:5010/api/users`
- **Get User**: `GET http://localhost:5010/api/users/{id}`

### Troubleshooting

#### Common Issues

**Redis Connection Failed**
```bash
# Start Redis with Docker
docker run -d -p 6379:6379 redis:latest

# Or install Redis locally
brew install redis  # macOS
redis-server        # Start Redis
```

**Port Already in Use**
```bash
# Check what's using the port
lsof -i :5010
lsof -i :3501

# Kill conflicting processes
pkill -f "DaprSample"
```

**Scheduler Connection Timeout**
- This is expected and harmless with `scheduler-host-address=""`
- Application will work normally despite the warning

## Troubleshooting

1. **"Unable to locate the Dapr CLI" error**
   - Install Dapr CLI following the prerequisites
   - Ensure `dapr` is in your PATH

2. **Port conflicts**
   - The sample uses ports 5000/5001 for API and 17045 for Aspire dashboard
   - Modify `launchSettings.json` if these ports are in use

3. **Redis connection issues**
   - Ensure Redis is running (automatically handled by Aspire)
   - Check the components configuration in `components/` directory
   - For development without Redis, use in-memory components (already configured)

4. **IMemoryCache dependency error**
   - The application requires `builder.Services.AddMemoryCache()` before `AddSekibanWithDapr()`
   - This has been fixed in the current implementation

## Development Configuration

### In-Memory Components (Default)
The sample is configured to use in-memory components for development:
- `components/pubsub.yaml` - In-memory pub/sub
- `components/statestore.yaml` - In-memory state store

### Redis Components (Production)
For production use with Redis:
1. Rename `pubsub-redis.yaml.bak` to `pubsub.yaml`
2. Rename `statestore-redis.yaml.bak` to `statestore.yaml`
3. Ensure Redis is running

## Implementation Details

This sample implements the envelope-based design to solve Dapr's interface serialization limitations:

1. **CommandEnvelope** - Wraps commands with Protobuf payloads for actor communication
2. **EventEnvelope** - Wraps events with Protobuf payloads
3. **EnvelopeAggregateActor** - Uses concrete envelope types for proper JSON serialization
4. **Protobuf serialization** - Ensures efficient binary encoding and type safety