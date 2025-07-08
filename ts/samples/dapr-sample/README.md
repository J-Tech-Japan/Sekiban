# Sekiban Dapr Sample

This sample demonstrates how to use Sekiban TypeScript with Dapr for distributed event sourcing.

## Overview

This sample implements a simple Task management system using:
- **Sekiban Core** for event sourcing abstractions
- **Sekiban Dapr** for distributed actor-based aggregates (optional)
- **PostgreSQL** for event persistence (when using Dapr)
- **Express.js** for REST API

## Project Structure

```
dapr-sample/
├── packages/
│   ├── domain/        # Domain model (events, commands, projectors)
│   ├── api/           # Express REST API
│   └── workflows/     # Business workflows (future)
├── dapr/              # Dapr configuration files
│   ├── components/    # State store and pubsub configuration
│   └── config.yaml    # Dapr runtime configuration
└── scripts/           # Helper scripts
```

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
- `DAPR_HTTP_PORT` - If set, enables Dapr mode
- `DATABASE_URL` - PostgreSQL connection string
- `API_PREFIX` - API route prefix (default: `/api`)

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