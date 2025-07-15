# Dapr Sample Project Structure

This document explains the consolidated project structure following DRY (Don't Repeat Yourself) principles.

## Directory Structure

```
dapr-sample/
├── dapr/                      # All Dapr configurations (single source of truth)
│   ├── config.yaml           # Dapr runtime configuration
│   └── components/           # Dapr component definitions
│       ├── statestore.yaml   # In-memory state store
│       └── pubsub.yaml       # In-memory pub/sub
├── docker-compose.yml        # Docker services (PostgreSQL, Redis)
├── packages/
│   ├── api/                  # API service
│   │   ├── src/             # API source code
│   │   ├── run-with-dapr.sh # References root dapr/ configs
│   │   └── run-dev-with-dapr.sh
│   ├── domain/              # Domain logic
│   └── workflows/           # Workflow definitions
└── scripts/                 # Utility scripts

```

## Key Principles

1. **Infrastructure at Root**: All infrastructure configuration (Dapr, Docker) lives at the root level
2. **Application Code in Packages**: Each package contains only application-specific code
3. **Scripts Reference Root**: Package scripts use relative paths to reference root configurations
4. **No Duplication**: Configuration files exist in only one place

## Running the Application

### Start Infrastructure (from root directory)
```bash
# Start PostgreSQL and Redis
docker-compose up -d

# Verify services are running
docker-compose ps
```

### Run API Service (from API package)
```bash
cd packages/api

# Development mode with hot reload
./run-dev-with-dapr.sh

# Production mode
./run-with-dapr.sh
```

## Configuration Files

### Dapr Configuration (`/dapr/config.yaml`)
- Actor configuration
- Tracing settings
- Feature flags

### Dapr Components (`/dapr/components/`)
- `statestore.yaml`: In-memory state store for development
- `pubsub.yaml`: In-memory pub/sub for development

### Docker Services (`/docker-compose.yml`)
- PostgreSQL: Event storage (future use)
- Redis: Caching and pub/sub (future use)

## Benefits of This Structure

1. **Single Source of Truth**: Update configurations in one place
2. **Clear Separation**: Infrastructure vs application code
3. **Easy Onboarding**: New developers can understand the structure quickly
4. **Consistent Environments**: Same configurations used everywhere
5. **Simplified CI/CD**: Clear paths for deployment configurations