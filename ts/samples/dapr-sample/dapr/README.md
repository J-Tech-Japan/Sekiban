# Dapr Configuration Structure

This directory contains Dapr configurations for different environments.

## Directory Structure

```
dapr/
├── config.yaml              # Dapr runtime configuration (shared)
├── components/              # Legacy components (being phased out)
├── components-dev/          # Development environment (in-memory)
│   ├── statestore.yaml     # In-memory state store
│   └── pubsub.yaml         # In-memory pub/sub
└── components-prod/         # Production environment
    ├── statestore.yaml     # PostgreSQL state store
    └── pubsub.yaml         # Redis pub/sub
```

## Usage

### Development Mode
Uses in-memory components for fast iteration:
```bash
cd packages/api
./run-dev-with-dapr.sh
```

### Production Mode
Uses PostgreSQL for state and Redis for pub/sub:
```bash
cd packages/api
./run-with-dapr.sh
```

## Key Differences

### Development (`components-dev/`)
- **State Store**: In-memory (no persistence)
- **Pub/Sub**: In-memory
- **Use Case**: Local development, testing
- **Requirements**: None (all in-memory)

### Production (`components-prod/`)
- **State Store**: PostgreSQL (persistent)
- **Pub/Sub**: Redis
- **Use Case**: Production, staging
- **Requirements**: PostgreSQL and Redis must be running

## Important Notes

1. Only one component can be configured as `actorStateStore: "true"`
2. The development setup doesn't require any external services
3. The production setup requires both PostgreSQL and Redis to be running