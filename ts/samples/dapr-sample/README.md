# Sekiban Dapr Sample

This sample demonstrates how to use Sekiban TypeScript with Dapr for distributed event sourcing.

## ⚠️ Important: Actor Integration Fixed

This sample has been updated with critical fixes for Dapr actor integration:
- Actor initialization order fixed (init before register)
- HTTP method handling (actors require PUT, not POST)
- Proper DaprClient usage with invoker.invoke()
- See [ACTOR_FIXES.md](./ACTOR_FIXES.md) for details

## Overview

This sample implements a simple Task management system using:
- **Sekiban Core** for event sourcing abstractions
- **Sekiban Dapr** for distributed actor-based aggregates
- **Event Store Options**: In-memory, PostgreSQL, or Azure Cosmos DB
- **Express.js** for REST API

## Project Structure

```
dapr-sample/
├── packages/
│   ├── domain/        # Domain model (events, commands, projectors)
│   ├── api/           # Express REST API
│   └── workflows/     # Business workflows (future)
├── dapr/              # Dapr configuration files (single source of truth)
│   ├── components/    # State store and pubsub configuration
│   └── config.yaml    # Dapr runtime configuration
├── docker-compose.yml # Docker services (PostgreSQL, Redis)
└── scripts/           # Helper scripts
```

**Note**: Following DRY principles, all infrastructure configuration (Dapr, Docker) is maintained at the root level. Package-level scripts reference these root configurations. See [STRUCTURE.md](./STRUCTURE.md) for details.

## Prerequisites

- Node.js 18+
- pnpm
- Dapr CLI installed and initialized
- PostgreSQL running (for event persistence)
- Optional: Redis (for production pubsub, sample uses in-memory)

## Quick Start

```bash
# Install dependencies
pnpm install

# Set up PostgreSQL (first time only)
./setup-postgres.sh

# Start with Dapr
./run-with-dapr.sh

# In another terminal, test the API
./test-api.sh
```

## API Endpoints

### Create Task
```bash
POST /api/tasks
{
  "title": "Task Title",
  "description": "Task Description",
  "priority": "high"
}
```

### Get Task
```bash
GET /api/tasks/:id
```

### List Tasks
```bash
GET /api/tasks
```

### Complete Task
```bash
PUT /api/tasks/:id/complete
```

## Domain Model

### Events
- `TaskCreated` - When a new task is created
- `TaskCompleted` - When a task is marked as complete
- `TaskPriorityChanged` - When task priority is updated

### Commands
- `CreateTask` - Creates a new task
- `CompleteTask` - Marks a task as complete
- `ChangePriority` - Updates task priority

### Aggregate
- `Task` - The task aggregate maintaining state through event projection

## How It Works

1. **Commands** are sent to the API endpoints
2. **SekibanDaprExecutor** routes commands to Dapr actors
3. **Dapr Actors** (from @sekiban/dapr) handle command execution
4. **Events** are persisted to PostgreSQL via actor state
5. **Projectors** apply events to rebuild aggregate state
6. **Queries** retrieve current state by replaying events

### Architecture
- Uses `SekibanDaprExecutor` from `@sekiban/dapr`
- Each aggregate becomes a Dapr virtual actor
- Events persisted to PostgreSQL through Dapr state store
- Supports distribution and scaling across multiple nodes
- Includes snapshot support for performance optimization

## Configuration

Environment variables (see `.env.example`):
- `STORAGE_TYPE` - Event store type: `inmemory`, `postgres`, or `cosmos` (default: `inmemory`)
- `DAPR_HTTP_PORT` - If set, enables Dapr mode
- `DATABASE_URL` - PostgreSQL connection string (required when `STORAGE_TYPE=postgres`)
- `COSMOS_CONNECTION_STRING` - Azure Cosmos DB connection string (required when `STORAGE_TYPE=cosmos`)
- `COSMOS_DATABASE_NAME` - Cosmos DB database name (default: `sekiban_events`)
- `API_PREFIX` - API route prefix (default: `/api`)

## Storage Options

### In-Memory (Default)
The simplest option for development and testing:
```bash
# No additional configuration needed
pnpm dev
```

### PostgreSQL
For production-ready persistence:
```bash
# Set up PostgreSQL first
./setup-postgres.sh

# Run with PostgreSQL
STORAGE_TYPE=postgres pnpm dev

# Or use the convenience script
cd packages/api && pnpm dev:postgres
```

### Azure Cosmos DB
For global distribution and automatic scaling:
```bash
# Set your Cosmos DB connection string
export COSMOS_CONNECTION_STRING="AccountEndpoint=https://your-account.documents.azure.com:443/;AccountKey=your-key;"

# Run with Cosmos DB
STORAGE_TYPE=cosmos pnpm dev

# Or use the convenience script
cd packages/api && pnpm dev:cosmos
```

## Development

```bash
# Build all packages
pnpm build

# Run in dev mode (with hot reload)
cd packages/api && pnpm dev

# Run tests
pnpm test
```

## Troubleshooting
- Ensure PostgreSQL is running and accessible
- Check Dapr placement service: `dapr run --app-id placement --placement-host-address localhost:50005`
- View Dapr logs: Add `--log-level debug` to the run command
- Actor registration errors: The Sekiban Dapr actors are internal to the framework

## Next Steps

1. Add more complex domain logic
2. Implement sagas for multi-aggregate workflows
3. Add query models and projections
4. Integrate with a frontend application