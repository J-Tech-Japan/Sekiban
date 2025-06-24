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