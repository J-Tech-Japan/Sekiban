# create-sekiban-app

Create a new Sekiban application with Dapr integration.

## Quick Start

```bash
npx create-sekiban-app my-app
cd my-app
pnpm build
pnpm dev
```

## What's Included

This template creates a complete Sekiban application with:

- **Domain Layer**: Domain models, commands, events, and projectors
- **API Layer**: Express.js REST API with Sekiban integration
- **Event Handlers**: Dapr actors for event processing
- **Multi-Projector**: Dapr actors for projections
- **Event Relay**: Service for event distribution
- **Workflows**: Dapr workflow integration

## Prerequisites

- Node.js 18 or later
- pnpm 8 or later
- Docker and Docker Compose
- Dapr CLI (optional, for local development)

## Project Structure

```
my-app/
├── packages/
│   ├── domain/          # Domain models and business logic
│   ├── api/             # REST API server
│   ├── api-event-handler/   # Event processing actors
│   ├── api-multi-projector/ # Projection actors
│   ├── event-relay/     # Event distribution service
│   └── workflows/       # Dapr workflows
├── dapr/                # Dapr configuration
│   ├── components/      # Dapr components (pubsub, statestore)
│   └── config.yaml      # Dapr configuration
├── scripts/             # Utility scripts
└── docker-compose.yml   # Docker services (PostgreSQL, Redis)
```

## Available Scripts

### In the root directory:

- `pnpm build` - Build all packages
- `pnpm dev` - Start development servers for all packages
- `pnpm test` - Run tests for all packages
- `pnpm services:start` - Start Docker services (PostgreSQL, Redis)
- `pnpm services:stop` - Stop Docker services

### Running with Dapr:

- `./run-all-services.sh` - Start all services with Dapr

## Configuration

### Environment Variables

Create a `.env` file in the root directory:

```env
# PostgreSQL
POSTGRES_HOST=localhost
POSTGRES_PORT=5432
POSTGRES_DB=sekiban_sample
POSTGRES_USER=postgres
POSTGRES_PASSWORD=postgres

# Redis
REDIS_HOST=localhost
REDIS_PORT=6379

# Dapr
DAPR_HTTP_PORT=3500
DAPR_GRPC_PORT=50001
```

### Storage Options

The template supports two storage modes:

1. **In-Memory** (default): Good for development and testing
2. **PostgreSQL**: For production use

To use PostgreSQL, set `USE_POSTGRES=true` when starting the API.

## Development Workflow

1. Start Docker services:
   ```bash
   pnpm services:start
   ```

2. Build the project:
   ```bash
   pnpm build
   ```

3. Start development servers:
   ```bash
   pnpm dev
   ```

4. Or run with Dapr:
   ```bash
   ./run-all-services.sh
   ```

## Testing

The template includes example tests. Run them with:

```bash
pnpm test
```

## Deployment

The application is designed to run in a Kubernetes environment with Dapr. See the [Dapr documentation](https://docs.dapr.io/operations/hosting/kubernetes/) for deployment instructions.

## Learn More

- [Sekiban Documentation](https://github.com/J-Tech-Japan/Sekiban)
- [Dapr Documentation](https://docs.dapr.io/)
- [Event Sourcing Pattern](https://docs.microsoft.com/en-us/azure/architecture/patterns/event-sourcing)

## License

MIT