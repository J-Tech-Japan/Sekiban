# API Multi-Projector Service

This service hosts the MultiProjectorActor for handling cross-aggregate projections in the Sekiban event sourcing framework.

## Overview

The MultiProjectorActor service is a dedicated Dapr service that runs MultiProjectorActor instances. These actors handle multi-projection queries that span across multiple aggregates.

## Configuration

- **Port**: 3003
- **Dapr HTTP Port**: 3503
- **Actor Type**: MultiProjectorActor

## Running the Service

### Start with Dapr

```bash
./start-multi-projector.sh
```

Or manually:

```bash
dapr run \
  --app-id dapr-sample-api-multi-projector \
  --app-port 3003 \
  --dapr-http-port 3503 \
  --resources-path ../../dapr/components \
  -- pnpm dev
```

## API Endpoints

- `GET /health` - Health check endpoint
- `GET /ready` - Readiness check endpoint
- `GET /api/v1/multi-projections/:projectorType/:id` - Get multi-projection state

## Environment Variables

- `PORT` - Application port (default: 3003)
- `DAPR_HTTP_PORT` - Dapr HTTP port (default: 3503)
- `USE_POSTGRES` - Use PostgreSQL event store (default: true)
- `DATABASE_URL` - PostgreSQL connection string

## Architecture

This service is part of the Dapr-based microservices architecture where different actor types run in separate services:

- **api** - Hosts AggregateActor
- **api-event-handler** - Hosts AggregateEventHandlerActor
- **api-multi-projector** - Hosts MultiProjectorActor (this service)

This separation allows for better scalability and isolation of concerns.