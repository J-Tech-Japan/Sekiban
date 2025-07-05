# Dapr Sample - Task Management System

This sample demonstrates how to use Sekiban with Dapr for building a distributed event-sourced application using the new schema-based type registry system.

## Architecture

The sample follows the recommended project structure:

```
dapr-sample/
├── packages/
│   ├── domain/        # Pure domain logic with schema-based types
│   ├── workflows/     # Application workflows
│   └── api/          # REST API with Dapr integration
```

## Key Features

- **Schema-Based Type System**: Uses Zod schemas for event, command, and projector definitions
- **PostgreSQL Event Store**: Events are persisted in PostgreSQL
- **Dapr State Management**: In-memory state store for local development
- **Dapr Pub/Sub**: In-memory pub/sub for event distribution
- **Dapr Actors**: Aggregate state managed by Dapr actors
- **Type Safety**: Full TypeScript type inference from schemas
- **Workflows**: Multi-step business processes

## Prerequisites

- Node.js >= 18
- pnpm >= 8
- Docker and Docker Compose
- Dapr CLI installed

## Setup

1. Clone the repository and navigate to the sample:
```bash
cd ts/samples/dapr-sample
```

2. Install dependencies:
```bash
pnpm install
```

3. Copy the environment file:
```bash
cp .env.example .env
```

4. Start PostgreSQL:
```bash
docker-compose up -d postgres
```

## Running the Application

### Quick Start (Simplified Demo)

For a quick demo without building all packages:
```bash
./run-simple.sh
```

This starts a simplified API server with in-memory storage that demonstrates the API structure.

### With Full Type System (Requires Building)

Once the main Sekiban packages are built:

1. Build all packages:
```bash
pnpm build
```

2. Start with Dapr:
```bash
pnpm dapr:api
```

### Without Dapr (Development Mode)

For development without Dapr:
```bash
cd packages/api && npx tsx watch src/server.ts
```

## API Endpoints

### Health Check
```bash
curl http://localhost:3000/health
```

### Create a Task
```bash
curl -X POST http://localhost:3000/api/tasks \
  -H "Content-Type: application/json" \
  -d '{
    "title": "Complete documentation",
    "description": "Write comprehensive API documentation",
    "priority": "high",
    "assignedTo": "john@example.com"
  }'
```

### Get a Task
```bash
curl http://localhost:3000/api/tasks/{taskId}
```

### Assign a Task
```bash
curl -X POST http://localhost:3000/api/tasks/{taskId}/assign \
  -H "Content-Type: application/json" \
  -d '{
    "assignedTo": "jane@example.com"
  }'
```

### Complete a Task
```bash
curl -X POST http://localhost:3000/api/tasks/{taskId}/complete \
  -H "Content-Type: application/json" \
  -d '{
    "completedBy": "jane@example.com",
    "notes": "All tests passed"
  }'
```

### Update a Task
```bash
curl -X PATCH http://localhost:3000/api/tasks/{taskId} \
  -H "Content-Type: application/json" \
  -d '{
    "title": "Updated title",
    "priority": "medium"
  }'
```

### Delete a Task
```bash
curl -X DELETE http://localhost:3000/api/tasks/{taskId} \
  -H "Content-Type: application/json" \
  -d '{
    "deletedBy": "admin@example.com",
    "reason": "Duplicate task"
  }'
```

### Run Workflow
```bash
curl -X POST http://localhost:3000/api/workflows/task-assignment \
  -H "Content-Type: application/json" \
  -d '{
    "title": "Review PR",
    "description": "Review and merge the feature branch",
    "assignedTo": "reviewer@example.com",
    "priority": "high"
  }'
```

## Project Structure

### Domain Package (`packages/domain`)

Contains pure domain logic with schema-based definitions:

- **Events**: Task lifecycle events (Created, Assigned, Completed, etc.)
- **Commands**: Operations that modify tasks
- **Projectors**: Build task state from events
- **Queries**: Read operations (to be implemented with read models)

### Workflows Package (`packages/workflows`)

Contains application-level workflows that orchestrate multiple commands:

- **TaskAssignmentWorkflow**: Creates and assigns a task in one operation

### API Package (`packages/api`)

REST API with Dapr integration:

- **Routes**: RESTful endpoints for task management
- **Executor Setup**: Configures Sekiban with Dapr and PostgreSQL
- **Middleware**: Error handling, validation, logging
- **Dapr Integration**: Actor configuration, pub/sub handling

## Schema-Based Type System

This sample demonstrates the new schema-based approach:

```typescript
// Define an event
export const TaskCreated = defineEvent({
  type: 'TaskCreated',
  schema: z.object({
    taskId: z.string().uuid(),
    title: z.string().min(1).max(200),
    // ... more fields
  })
});

// Define a command
export const CreateTask = defineCommand({
  type: 'CreateTask',
  schema: z.object({
    title: z.string().min(1).max(200),
    // ... more fields
  }),
  aggregateType: 'Task',
  handlers: {
    specifyPartitionKeys: () => PartitionKeys.generate('Task'),
    validate: (data) => /* business validation */,
    handle: (data, aggregate) => /* return events */
  }
});

// Define a projector
export const taskProjector = defineProjector<TaskPayload | EmptyAggregatePayload>({
  aggregateType: 'Task',
  initialState: () => ({ aggregateType: 'Empty' }),
  projections: {
    TaskCreated: (state, event) => /* return new state */,
    // ... more projections
  }
});
```

## Testing

Run tests for all packages:
```bash
pnpm test
```

## Cleanup

Stop all services:
```bash
pnpm dapr:stop
```

## Next Steps

1. **Add Read Models**: Implement projections for queries
2. **Add Authentication**: Secure API endpoints
3. **Add Monitoring**: Integrate OpenTelemetry
4. **Add More Workflows**: Implement complex business processes
5. **Add Integration Tests**: Test full command-to-query flow

## Troubleshooting

### Dapr sidecar not starting
- Ensure Dapr is installed: `dapr --version`
- Initialize Dapr: `dapr init`

### PostgreSQL connection issues
- Check if PostgreSQL is running: `docker ps`
- Verify connection string in `.env`

### Type errors
- Rebuild the project: `pnpm build`
- Ensure all packages are linked: `pnpm install`