# Sekiban Dapr Sample

This sample demonstrates how to use Sekiban with Dapr and .NET Aspire.

## Prerequisites

- .NET 9.0 SDK
- Docker Desktop
- Dapr CLI (install from https://docs.dapr.io/getting-started/install-dapr-cli/)

## Running the Sample

1. Install Dapr runtime:
   ```bash
   dapr init
   ```

2. Run the Aspire AppHost:
   ```bash
   dotnet run --project DaprSample.AppHost
   ```

3. The API will be available at http://localhost:5000 (or check the Aspire dashboard)

## Testing the API

### Create a User
```bash
curl -X POST http://localhost:5000/api/users/create \
  -H "Content-Type: application/json" \
  -d '{
    "userId": "550e8400-e29b-41d4-a716-446655440000",
    "name": "John Doe",
    "email": "john@example.com"
  }'
```

### Update User Name
```bash
curl -X POST http://localhost:5000/api/users/550e8400-e29b-41d4-a716-446655440000/update-name \
  -H "Content-Type: application/json" \
  -d '{
    "newName": "Jane Doe"
  }'
```

### Get User
```bash
curl http://localhost:5000/api/users/550e8400-e29b-41d4-a716-446655440000
```

## Architecture

This sample uses:
- **Dapr Actors** for aggregate state management
- **Redis** as the state store and pub/sub broker
- **Sekiban.Pure.Dapr** for event sourcing integration
- **.NET Aspire** for orchestration and observability