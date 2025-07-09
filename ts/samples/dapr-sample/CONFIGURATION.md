# Dapr Sample Configuration

This document describes the consolidated configuration structure for the Dapr sample application.

## Directory Structure

```
dapr-sample/
├── dapr/                       # Consolidated Dapr configuration
│   ├── config.yaml            # Main Dapr configuration with actor support
│   └── components/            # Dapr components
│       ├── pubsub.yaml        # In-memory pub/sub
│       ├── statestore.yaml    # In-memory state store (default)
│       └── statestore-postgres.yaml  # PostgreSQL state store (optional)
├── docker-compose.yml         # Services configuration (PostgreSQL, Redis)
├── package.json              # Root package with consolidated scripts
└── packages/
    └── api/
        ├── run-with-dapr.sh  # API-specific Dapr launcher (uses root configs)
        └── run-dev-with-dapr.sh  # Development mode launcher
```

## Quick Start

1. **Start services** (PostgreSQL and Redis):
   ```bash
   pnpm services:start
   # Or specific services:
   pnpm postgres:start
   pnpm redis:start
   ```

2. **Start Dapr placement service** (required for actors):
   ```bash
   pnpm dapr:placement
   # Or directly:
   ./start-dapr-placement.sh
   ```

3. **Run the API with Dapr**:
   ```bash
   pnpm dapr:start
   # Or directly:
   ./run-with-dapr.sh
   ```

## Configuration Details

### Dapr Configuration (`dapr/config.yaml`)
- Actor support enabled with 1-hour idle timeout
- Zipkin tracing configured
- gRPC proxy enabled
- Metrics enabled

### Components
- **pubsub.yaml**: In-memory pub/sub for development
- **statestore.yaml**: In-memory state store with actor support
- **statestore-postgres.yaml**: PostgreSQL state store for production-like setup

### Docker Services
- **PostgreSQL**: Port 5432, database: sekiban_events
- **Redis**: Port 6379 (for future pub/sub or caching)

## Script Commands

### Root Level (`pnpm` commands)
- `pnpm dapr:start` - Start API with Dapr using consolidated configs
- `pnpm dapr:placement` - Start Dapr placement service
- `pnpm services:start` - Start all Docker services
- `pnpm services:stop` - Stop all Docker services
- `pnpm postgres:start/stop` - Manage PostgreSQL
- `pnpm redis:start/stop` - Manage Redis

### API Level (from `packages/api/`)
- `./run-with-dapr.sh` - Production mode with Dapr
- `./run-dev-with-dapr.sh` - Development mode with Dapr

## Benefits of Consolidation

1. **Single source of truth**: All Dapr configurations in one place
2. **Easier maintenance**: Update configurations once, use everywhere
3. **Consistent environment**: Same components and settings across all runs
4. **Flexible deployment**: Easy to switch between in-memory and PostgreSQL storage