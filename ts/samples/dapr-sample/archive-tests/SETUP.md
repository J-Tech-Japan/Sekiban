# Sekiban Dapr Sample Setup

This sample demonstrates Sekiban event sourcing with Dapr actors for distributed aggregate management.

## Prerequisites

1. **Dapr CLI**: Install from https://docs.dapr.io/getting-started/install-dapr-cli/
2. **PostgreSQL**: Must be running on localhost:5432
3. **Redis**: For pubsub (optional - can use in-memory)
4. **Node.js**: Version 18 or higher
5. **pnpm**: Package manager

## Quick Start

```bash
# 1. Run the all-in-one setup script
./start-all.sh

# 2. In another terminal, test the API
./test-api.sh
```

## Manual Setup

### 1. Database Setup
```bash
# Set up PostgreSQL database and user
./setup-postgres.sh
```

### 2. Environment Setup
```bash
# Copy environment template
cp .env.example .env
```

### 3. Build Project
```bash
pnpm install
pnpm build
```

### 4. Start Dapr Placement (for actors)
```bash
./start-dapr-placement.sh
```

### 5. Run with Dapr
```bash
./run-with-dapr.sh
```

## Architecture

- **Dapr Actors**: Each aggregate is a Dapr actor instance
- **PostgreSQL**: Stores events and actor state
- **Event Sourcing**: Commands generate events that are persisted
- **CQRS**: Commands and queries are handled separately

## Testing

Use the test script to create tasks and verify the system:
```bash
./test-api.sh
```

## Troubleshooting

1. **PostgreSQL connection errors**: 
   - Check if PostgreSQL is running
   - Verify connection settings in `.env`

2. **Dapr actor errors**:
   - Ensure placement service is running on port 50005
   - Check Dapr logs with `--log-level debug`

3. **Build errors**:
   - Run `pnpm install` to ensure dependencies are installed
   - Check TypeScript errors with `pnpm build`